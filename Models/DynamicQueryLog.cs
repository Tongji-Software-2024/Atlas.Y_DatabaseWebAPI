using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DatabaseWebAPI.Models;

[Table("DYNAMIC_QUERY_LOG")]
public sealed class DynamicQueryLog
{
    [Key]
    [Column("LOG_ID")]
    public string LogId { get; set; } = string.Empty;

    [Required]
    [Column("QUERY_TIME")]
    public DateTime QueryTime { get; set; }

    [Required]
    [Column("TARGET_PRO_SEQ")]
    public string TargetProSeq { get; set; } = string.Empty;

    [Required]
    [Column("TARGET_PRO_PDB")]
    public string TargetProPdb { get; set; } = string.Empty;

    [Required]
    [Column("TARGET_POSITION")]
    public string TargetPosition { get; set; } = string.Empty;

    [Required]
    [Column("LINKER_MECH")]
    public string LinkerMech { get; set; } = string.Empty;

    [Required]
    [Column("LINKER_SOLU")]
    public string LinkerSolu { get; set; } = string.Empty;

    [Required]
    [Column("LIGHT_INDUCTION")]
    public string LightInduction { get; set; } = string.Empty;
}