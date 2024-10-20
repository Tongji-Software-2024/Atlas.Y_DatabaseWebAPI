using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DatabaseWebAPI.Data;
using DatabaseWebAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Renci.SshNet;

namespace DatabaseWebAPI.Controllers;

[Route("api/basic-prediction")]
[ApiController]
public partial class BasicDesigner(OracleDbContext context) : ControllerBase
{
    [HttpPost]
    public Task<ActionResult<IEnumerable<QueryLog>>> Predict([FromBody] QueryLog queryLog)
    {
        using var client = new SshClient("[TODO: Host]", "[TODO: Post]", "[TODO: Username]", "[TODO: Password]");
        try
        {
            client.Connect();

            var outputPath = $"/root/autodl-tmp/fusion_pro/output/{queryLog.LogId}";
            client.RunCommand($"mkdir -p {outputPath}/pdb");

            var base64String = queryLog.TargetProPdb;
            const int chunkSize = 10240;
            var base64FilePath = $"{outputPath}/target_protein_base64.txt";
            foreach (var chunk in SplitStringIntoChunks(base64String, chunkSize))
            {
                var encodedChunk = Convert.ToBase64String(Encoding.UTF8.GetBytes(chunk));
                var chunkCmd = $"echo \"{encodedChunk}\" | base64 --decode >> {base64FilePath}";
                client.RunCommand(chunkCmd);
            }

            var decodePdbCmd = $"cat {base64FilePath} | base64 --decode > {outputPath}/target_protein.pdb";
            client.RunCommand(decodePdbCmd);

            var signalPeptides = context.SignalPeptideSet
                .Where(sp => sp.Localization == queryLog.TargetPosition)
                .ToList();

            foreach (var sp in signalPeptides)
            {
                var line = $"{sp.SpId}\t{sp.SpSeq}";
                var signalsCmd = $"echo \"{line}\" >> {outputPath}/signals.txt";
                client.RunCommand(signalsCmd);
            }

            var linkers = context.LinkerSet
                .Where(l => l.MechanicalProperty == queryLog.LinkerMech && l.Solubility == queryLog.LinkerSolu)
                .ToList();

            foreach (var linker in linkers)
            {
                var line = $"{linker.LinkerId}\t{linker.LinkerSeq}";
                var linkersCmd = $"echo \"{line}\" >> {outputPath}/linkers.txt";
                client.RunCommand(linkersCmd);
            }

            var proteinSequenceCmd = $"echo \"{queryLog.TargetProSeq}\" > {outputPath}/target_protein.txt";
            client.RunCommand(proteinSequenceCmd);

            var predictionCmd = $"source /root/miniconda3/etc/profile.d/conda.sh && \\" +
                                $"conda activate esmfold && \\" +
                                $"python /root/autodl-tmp/fusion_pro/fusion_adjust.py \\" +
                                $"-s /root/autodl-tmp/fusion_pro/output/{queryLog.LogId}/signals.txt \\" +
                                $"-l /root/autodl-tmp/fusion_pro/output/{queryLog.LogId}/linkers.txt \\" +
                                $"-t /root/autodl-tmp/fusion_pro/output/{queryLog.LogId}/target_protein.txt \\" +
                                $"-o /root/autodl-tmp/fusion_pro/output/{queryLog.LogId}/result.tsv";
            client.RunCommand(predictionCmd);

            var resultFile = $"{outputPath}/result.tsv";
            var resultCmd = $"cat {resultFile}";
            var result = client.RunCommand(resultCmd).Result;
            var lines = result.Split("\n", StringSplitOptions.RemoveEmptyEntries);
            var results = new List<dynamic>();

            foreach (var line in lines.Skip(1))
            {
                var columns = line.Split('\t');
                if (columns.Length == 5)
                {
                    results.Add(new
                    {
                        fpId = columns[0],
                        signalId = columns[1],
                        linkerId = columns[2],
                        fusionProtein = columns[3],
                        stabilityScore = decimal.Parse(columns[4], CultureInfo.InvariantCulture),
                        signal = context.SignalPeptideSet.FirstOrDefault(sp => sp.SpId == columns[1])?.SpSeq,
                        linker = context.LinkerSet.FirstOrDefault(l => l.LinkerId == columns[2])?.LinkerSeq
                    });
                }
            }

            return Task.FromResult<ActionResult<IEnumerable<QueryLog>>>(Ok(results));
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    [HttpGet("get-file/{*filename}")]
    public ActionResult<string> GetFile(string filename)
    {
        using var client = new SshClient("[TODO: Host]", "[TODO: Post]", "[TODO: Username]", "[TODO: Password]");
        try
        {
            client.Connect();

            var filePath = $"/root/autodl-tmp/fusion_pro/output/{filename}";
            var commandText = $"cat {filePath}";
            var command = client.RunCommand(commandText);

            if (command.ExitStatus == 0)
            {
                var fileContents = command.Result;
                var fileContentsAsBytes = Encoding.UTF8.GetBytes(fileContents);
                var base64EncodedFile = Convert.ToBase64String(fileContentsAsBytes);
                return Ok(base64EncodedFile);
            }
            else
            {
                return NotFound($"File not found: {filePath}");
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    [HttpGet("get-stability/{*filename}")]
    public ActionResult<double[]> GetStability(string filename)
    {
        using var client = new SshClient("[TODO: Host]", "[TODO: Post]", "[TODO: Username]", "[TODO: Password]");
        try
        {
            client.Connect();

            var filePath = $"/root/autodl-tmp/fusion_pro/output/{filename}";
            var scriptCommand = $"source /root/miniconda3/etc/profile.d/conda.sh && \\" +
                                $"conda activate esmfold && \\" +
                                $"python /root/autodl-tmp/fusion_pro/stability/stability.py -i {filePath}";
            var commandResult = client.RunCommand(scriptCommand);

            if (commandResult.ExitStatus == 0)
            {
                var resultContent = commandResult.Result;
                var regex = MyRegex();
                var match = regex.Match(resultContent);
                var scoresContent = match.Groups[1].Value;
                var scores = scoresContent.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(double.Parse)
                    .ToArray();
                return Ok(scores);
            }
            else
            {
                return NotFound($"File not found: {filePath}");
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    [HttpGet("get-primary-stability/{*filename}")]
    public ActionResult<double> GetPrimaryStability(string filename)
    {
        using var client = new SshClient("[TODO: Host]", "[TODO: Post]", "[TODO: Username]", "[TODO: Password]");
        try
        {
            client.Connect();

            var filePath = $"/root/autodl-tmp/fusion_pro/output/{filename}";
            var scriptCommand = $"source /root/miniconda3/etc/profile.d/conda.sh && \\" +
                                $"conda activate esmfold && \\" +
                                $"python /root/autodl-tmp/fusion_pro/stability/stability.py -i {filePath} -t";
            var commandResult = client.RunCommand(scriptCommand);

            if (commandResult.ExitStatus == 0)
            {
                var resultContent = commandResult.Result;
                var regex = MyRegex();
                var match = regex.Match(resultContent);
                var scoresContent = match.Groups[1].Value;
                var score = double.Parse(scoresContent);
                return Ok(score);
            }
            else
            {
                return NotFound($"File not found: {filePath}");
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    [HttpPost("get-variant-stability")]
    public ActionResult<double> GetVariantStability([FromBody] VrStabilityRequsetDto inputDto)
    {
        using var client = new SshClient("[TODO: Host]", "[TODO: Post]", "[TODO: Username]", "[TODO: Password]");
        try
        {
            client.Connect();

            var scriptCommand = $"source /root/miniconda3/etc/profile.d/conda.sh && \\" +
                                $"conda activate esmfold && \\" +
                                $"python /root/autodl-tmp/model_zero_sample/calculate_stability.py \\" +
                                $"-s {inputDto.Sequence} \\" +
                                $"-v {inputDto.VariantId} \\" +
                                $"-o /root/autodl-tmp/model_zero_sample/output/{inputDto.LogId} \\" +
                                $"-f {inputDto.FpId}";
            var commandResult = client.RunCommand(scriptCommand);

            if (commandResult.ExitStatus == 0)
            {
                var resultContent = commandResult.Result;
                var regex = MyRegex();
                var match = regex.Match(resultContent);
                var scoresContent = match.Groups[1].Value;
                var score = double.Parse(scoresContent);
                return Ok(score);
            }
            else
            {
                return NotFound();
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    [HttpGet("get-global-cad-score/{logId}/{fpId}")]
    public ActionResult<double> GetGlobalCadScore(string logId, string fpId)
    {
        using var client = new SshClient("[TODO: Host]", "[TODO: Post]", "[TODO: Username]", "[TODO: Password]");
        try
        {
            client.Connect();

            var scriptCommand = $"source /root/miniconda3/etc/profile.d/conda.sh && \\" +
                                $"conda activate base && \\" +
                                $"python /root/autodl-tmp/fusion_pro/CAD-score/cad_bash.py -t /root/autodl-tmp/fusion_pro/output/{logId}/target_protein.pdb -f /root/autodl-tmp/fusion_pro/output/{logId}/pdb/{fpId}.pdb -g";
            var commandResult = client.RunCommand(scriptCommand);

            if (commandResult.ExitStatus == 0)
            {
                var resultContent = commandResult.Result;
                var regex = MyRegex();
                var match = regex.Match(resultContent);
                var scoresContent = match.Groups[1].Value;
                var score = double.Parse(scoresContent);
                return Ok(score);
            }
            else
            {
                return NotFound();
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    [HttpGet("get-local-cad-score/{logId}/{fpId}/{chainName}/{residueIndex:int}/{radius:double}")]
    public ActionResult<double[]> GetLocalCadScore(string logId, string fpId, string chainName, int residueIndex,
        double radius)
    {
        using var client = new SshClient("[TODO: Host]", "[TODO: Post]", "[TODO: Username]", "[TODO: Password]");
        try
        {
            client.Connect();

            var scriptCommand = $"source /root/miniconda3/etc/profile.d/conda.sh && \\" +
                                $"conda activate base && \\" +
                                $"python /root/autodl-tmp/fusion_pro/CAD-score/cad_bash.py -t /root/autodl-tmp/fusion_pro/output/{logId}/target_protein.pdb -f /root/autodl-tmp/fusion_pro/output/{logId}/pdb/{fpId}.pdb -l -c {chainName},{residueIndex} -ra {radius}";
            var commandResult = client.RunCommand(scriptCommand);

            if (commandResult.ExitStatus == 0)
            {
                var resultContent = commandResult.Result;
                var regex = MyRegex();
                var match = regex.Match(resultContent);
                var scoresContent = match.Groups[1].Value;
                var scores = scoresContent.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(double.Parse)
                    .ToArray();
                return Ok(scores);
            }
            else
            {
                return NotFound();
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    [HttpPost("sequence-optimization")]
    public Task<ActionResult<IEnumerable<object>>> SequenceOptimization([FromBody] SeqOptRequsetDto inputDto)
    {
        using var client = new SshClient("[TODO: Host]", "[TODO: Post]", "[TODO: Username]", "[TODO: Password]");
        try
        {
            client.Connect();

            var scriptCommand = $"""
                                 source /root/miniconda3/etc/profile.d/conda.sh && \
                                 conda activate protlgn && \
                                 python /root/autodl-tmp/model_zero_sample/data_mutant_adjust.py \
                                 --fasta_sequence {inputDto.FastaSequence} \
                                 --pdb_file /root/autodl-tmp/fusion_pro/output/{inputDto.LogId}/pdb/{inputDto.FpId}.pdb \
                                 --mutant_dataset /root/autodl-tmp/model_zero_sample/output/{inputDto.LogId}/DATASET \
                                 --fpid {inputDto.FpId} && \
                                 CUDA_VISIBLE_DEVICES=0 python /root/autodl-tmp/model_zero_sample/mutant_predict_adjust.py \
                                 --checkpoint /root/autodl-tmp/model_zero_sample/ckpt/{inputDto.ModelName}.pt \
                                 --c_alpha_max_neighbors 10 \
                                 --gnn egnn \
                                 --use_sasa \
                                 --layer_num 6 \
                                 --gnn_config /root/autodl-tmp/model_zero_sample/src/Egnnconfig/egnn_mutant.yaml \
                                 --mutant_dataset /root/autodl-tmp/model_zero_sample/output/{inputDto.LogId}/DATASET \
                                 --fpid {inputDto.FpId}
                                 """;
            client.RunCommand(scriptCommand);

            var resultFile = $"/root/autodl-tmp/model_zero_sample/output/{inputDto.LogId}/result.tsv";
            var resultCmd = $"cat {resultFile}";
            var result = client.RunCommand(resultCmd).Result;
            var lines = result.Split("\n", StringSplitOptions.RemoveEmptyEntries);
            var results = new List<dynamic>();

            foreach (var line in lines.Skip(1))
            {
                var columns = line.Split('\t');
                if (columns.Length == 5)
                {
                    results.Add(new
                    {
                        variantId = columns[0],
                        predScore = columns[1],
                        mutationSite = columns[2],
                        primarySeq = columns[3],
                        variantSeq = columns[4]
                    });
                }
            }

            return Task.FromResult<ActionResult<IEnumerable<object>>>(Ok(results));
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    [HttpGet("get-functionality-score/{logId}/{fpId}/{vrId}")]
    public ActionResult<double> GetFunctionalityScore(string logId, string fpId, string vrId)
    {
        using var client = new SshClient("[TODO: Host]", "[TODO: Post]", "[TODO: Username]", "[TODO: Password]");
        try
        {
            client.Connect();

            var scriptCommand = $"source /root/miniconda3/etc/profile.d/conda.sh && \\" +
                                $"conda activate base && \\" +
                                $"python /root/autodl-tmp/fusion_pro/CAD-score/cad_bash.py -t /root/autodl-tmp/fusion_pro/output/{logId}/pdb/{fpId}.pdb -f /root/autodl-tmp/model_zero_sample/output/{logId}/DATASET/DATASET/{fpId}/{vrId}.pdb -g";
            var commandResult = client.RunCommand(scriptCommand);

            if (commandResult.ExitStatus == 0)
            {
                var resultContent = commandResult.Result;
                var regex = MyRegex();
                var match = regex.Match(resultContent);
                var scoresContent = match.Groups[1].Value;
                var score = double.Parse(scoresContent);
                return Ok(score);
            }
            else
            {
                return NotFound();
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    public IEnumerable<string> SplitStringIntoChunks(string str, int chunkSize)
    {
        for (var i = 0; i < str.Length; i += chunkSize)
        {
            if (i + chunkSize > str.Length)
            {
                chunkSize = str.Length - i;
            }

            yield return str.Substring(i, chunkSize);
        }
    }

    public class SeqOptRequsetDto
    {
        public string FastaSequence { get; set; }
        public string LogId { get; set; }
        public string FpId { get; set; }
        public string ModelName { get; set; }
    }

    public class VrStabilityRequsetDto
    {
        public string Sequence { get; set; }
        public string VariantId { get; set; }
        public string LogId { get; set; }
        public string FpId { get; set; }
    }

    [GeneratedRegex("FLAG(.*)FLAG")]
    private static partial Regex MyRegex();
}