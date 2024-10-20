using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DatabaseWebAPI.Models;

[Table("SIGNAL_PEPTIDE")]
public sealed class SignalPeptide
{
    [Key]
    [Column("SP_ID")]
    public string SpId { get; set; } = string.Empty;

    [Required]
    [Column("SP_SEQ")]
    public string SpSeq { get; set; } = string.Empty;

    [Required]
    [Column("LOCALIZATION")]
    public string Localization { get; set; } = string.Empty;
}