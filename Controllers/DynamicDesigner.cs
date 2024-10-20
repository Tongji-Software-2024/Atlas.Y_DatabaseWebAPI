using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DatabaseWebAPI.Data;
using DatabaseWebAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Renci.SshNet;

namespace DatabaseWebAPI.Controllers;

[Route("api/dynamic-prediction")]
[ApiController]
public partial class DynamicDesigner(OracleDbContext context) : ControllerBase
{
    [HttpPost]
    public Task<ActionResult<IEnumerable<DynamicQueryLog>>> Predict([FromBody] DynamicQueryLog queryLog)
    {
        using var client = new SshClient("[TODO: Host]", "[TODO: Post]", "[TODO: Username]", "[TODO: Password]");
        try
        {
            client.Connect();

            var outputPath = $"/root/autodl-tmp/dynamic/output/{queryLog.LogId}";
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

            var lightInductionCmd = queryLog.LightInduction switch
            {
                "blue" =>
                    $"echo \"DS0001\tCRY2\tMKMDKKTIVWFRRDLRIEDNPALAAAAHEGSVFPVFIWCPEEEGQFYPGRASRWWMKQSLAHLSQSLKALGSDLTLIKTHNTISAILDCIRVTGATKVVFNHLYDPVSLVRDHTVKEKLVERGISVQSYNGDLLYEPWEIYCEKGKPFTSFNSYWKKCLDMSIESVMLPPPWRLMPITAAAEAIWACSIEELGLENEAEKPSNALLTRAWSPGWSNADKLLNEFIEKQLIDYAKNSKKVVGNSTSLLSPYLHFGEISVRHVFQCARMKQIIWARDKNSEGEESADLFLRGIGLREYSRYICFNFPFTHEQSLLSHLRFFPWDADVDKFKAWRQGRTGYPLVDAGMRELWATGWMHNRIRVIVSSFAVKFLLLPWKWGMKYFWDTLLDADLECDILGWQYISGSIPDGHELDRLDNPALQGAKYDPEGEYIRQWLPELARLPTEWIHHPWDAPLTVLKASGVELGTNYAKPIVDIDTARELLAKAISRTREAQIMIGAAPDEIVADSFEALGANTIKEPGLCPSVSSNDQQVPSAVRYNGSKRVKPEEEEERDMKKSRGFDERELFSTAESSSSSSVFFVSQSCSLASEGKNLEGIQDSSDQITTSLGKNGCK\teGFP\tTGRHHGEQGRGAVHRGGAHPGRAGRRRKRPQVQRVRRGRGRCHLRQADPEVHLHHRQAARALAHPRDHPDLRRAVLQPLPRPHEAARLLQVRHARRLRPGAHHLLQGRRQLQDPRRGEVRGRHPGEPHRAEGHRLQGGRQHPGAQAGVQLQQPQRLYHGRQAEERHQGELQDPPQHRGRQRAARRPLPAEHPHRRRPRAAARQPLPEHPVRPEQRPQREARSHGPAGVRDRRRDHSRHGR\" > {outputPath}/dynamic.txt",
                "red" =>
                    $"echo \"DS0002\tPIF3\tMMFLPTDYCCRLSDQEYMELVFENGQILAKGQRSNVSLHNQRTKSIMDLYEAEYNEDFMKSIIHGGGGAITNLGDTQVVPQSHVAAAHETNMLESNKHVD\tmKO\tMVSVIKPEMKMRYYMDGSVNGHEFTIEGEGTGRPYEGHQEMTLRVTMAKGGPMPFAFDLVSHVFCYGHRPFTKYPEEIPDYFKQAFPEGLSWERSLEFEDGGSASVSAHISLRGNTFYHKSKFTGVNFPADGPIMQNQSVDWEPSTEKITASDGVLKGDVTMYLKLEGGGNHKCQFKTTYKAAKKILKMPGSHYISHRLVRKTEGNITELVEDAVAHS\" > {outputPath}/dynamic.txt",
                "far-red" =>
                    $"echo \"DS0003\tQPAS1\tHSRLIAAQQAMERDYWRLRELETRYRLVFDAAADAVMIVSAGDMRIVEANRAAVNAISRVERGNDDLAGRDFLAEVAAADRDAVRDMLAQVRQRGTALSVLVHLGRYDRAWMLRGSLMSSERRQVFLLHFTPVTTTPAID\tmVenus\tMVSKGEELFTGVVPILVELDGDVNGHKFSVSGEGEGDATYGKLTLKLICTTGKLPVPWPTLVTTLGYGLQCFARYPDHMKQHDFFKSAMPEGYVQERTIFFKDDGNYKTRAEVKFEGDTLVNRIELKGIDFKEDGNILGHKLEYNYNSHNVYITADKQKNGIKANFKIRHNIEDGGVQLADHYQQNTPIGDGPVLLPDNHYLSYQSKLSKDPNEKRDHMVLLEFVTAAGITLGMDELYKHHHHHH\" > {outputPath}/dynamic.txt",
                _ => ""
            };
            client.RunCommand(lightInductionCmd);

            var predictionCmd = $"source /root/miniconda3/etc/profile.d/conda.sh && \\" +
                                $"conda activate esmfold && \\" +
                                $"python /root/autodl-tmp/dynamic/dynamic_fusion.py \\" +
                                $"-d /root/autodl-tmp/dynamic/output/{queryLog.LogId}/dynamic.txt \\" +
                                $"-l /root/autodl-tmp/dynamic/output/{queryLog.LogId}/linkers.txt \\" +
                                $"-t /root/autodl-tmp/dynamic/output/{queryLog.LogId}/target_protein.txt \\" +
                                $"-o /root/autodl-tmp/dynamic/output/{queryLog.LogId}/result.tsv";
            client.RunCommand(predictionCmd);

            var resultFile = $"{outputPath}/result.tsv";
            var resultCmd = $"cat {resultFile}";
            var result = client.RunCommand(resultCmd).Result;
            var lines = result.Split("\n", StringSplitOptions.RemoveEmptyEntries);
            var results = new List<dynamic>();

            foreach (var line in lines.Skip(1))
            {
                var columns = line.Split('\t');
                if (columns.Length == 4)
                {
                    results.Add(new
                    {
                        dfpId = columns[0],
                        linkerId = columns[1],
                        fusionProtein = columns[2],
                        stabilityScore = decimal.Parse(columns[3], CultureInfo.InvariantCulture),
                        linker = context.LinkerSet.FirstOrDefault(l => l.LinkerId == columns[1])?.LinkerSeq
                    });
                }
            }

            return Task.FromResult<ActionResult<IEnumerable<DynamicQueryLog>>>(Ok(results));
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

            var filePath = $"/root/autodl-tmp/dynamic/output/{filename}";
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

            var filePath = $"/root/autodl-tmp/dynamic/output/{filename}";
            var scriptCommand = $"source /root/miniconda3/etc/profile.d/conda.sh && \\" +
                                $"conda activate esmfold && \\" +
                                $"python /root/autodl-tmp/dynamic/stability/stability.py -i {filePath}";
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

    [HttpGet("get-global-cad-score/{logId}/{fpId}")]
    public ActionResult<double> GetGlobalCadScore(string logId, string fpId)
    {
        using var client = new SshClient("[TODO: Host]", "[TODO: Post]", "[TODO: Username]", "[TODO: Password]");
        try
        {
            client.Connect();

            var scriptCommand = $"source /root/miniconda3/etc/profile.d/conda.sh && \\" +
                                $"conda activate base && \\" +
                                $"python /root/autodl-tmp/dynamic/CAD-score/cad_bash.py -t /root/autodl-tmp/dynamic/output/{logId}/target_protein.pdb -f /root/autodl-tmp/dynamic/output/{logId}/pdb/{fpId}.pdb -g";
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

    [HttpGet("get-local-cad-score/{logId}/{fpId}/{chainName}/{residueIndex:int}/{radius:double}/{lightInduction}")]
    public ActionResult<double[]> GetLocalCadScore(string logId, string fpId, string chainName, int residueIndex,
        double radius, string lightInduction)
    {
        using var client = new SshClient("[TODO: Host]", "[TODO: Post]", "[TODO: Username]", "[TODO: Password]");
        try
        {
            client.Connect();

            var scriptCommand = lightInduction switch
            {
                "blue" =>
                    $"source /root/miniconda3/etc/profile.d/conda.sh && conda activate base && python /root/autodl-tmp/dynamic/CAD-score/cad_local.py -t /root/autodl-tmp/dynamic/CAD-score/CRY2.pdb /root/autodl-tmp/dynamic/output/{logId}/target_protein.pdb /root/autodl-tmp/dynamic/CAD-score/eGFP.pdb -f /root/autodl-tmp/dynamic/output/{logId}/pdb/{fpId}.pdb -l -c {chainName},{residueIndex} -ra {radius}",
                "red" =>
                    $"source /root/miniconda3/etc/profile.d/conda.sh && conda activate base && python /root/autodl-tmp/dynamic/CAD-score/cad_local.py -t /root/autodl-tmp/dynamic/CAD-score/PIF3.pdb /root/autodl-tmp/dynamic/output/{logId}/target_protein.pdb /root/autodl-tmp/dynamic/CAD-score/mKO.pdb -f /root/autodl-tmp/dynamic/output/{logId}/pdb/{fpId}.pdb -l -c {chainName},{residueIndex} -ra {radius}",
                "far-red" =>
                    $"source /root/miniconda3/etc/profile.d/conda.sh && conda activate base && python /root/autodl-tmp/dynamic/CAD-score/cad_local.py -t /root/autodl-tmp/dynamic/CAD-score/QPAS1.pdb /root/autodl-tmp/dynamic/output/{logId}/target_protein.pdb /root/autodl-tmp/dynamic/CAD-score/mVenus.pdb -f /root/autodl-tmp/dynamic/output/{logId}/pdb/{fpId}.pdb -l -c {chainName},{residueIndex} -ra {radius}",
                _ => ""
            };
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

    [GeneratedRegex("FLAG(.*)FLAG")]
    private static partial Regex MyRegex();
}