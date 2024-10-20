using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DatabaseWebAPI.Models;

[Table("LINKER")]
public sealed class Linker
{
    [Key]
    [Column("LINKER_ID")]
    public string LinkerId { get; set; } = string.Empty;

    [Required]
    [Column("LINKER_SEQ")]
    public string LinkerSeq { get; set; } = string.Empty;

    [Required]
    [Column("MECHANICAL_PROPERTY")]
    public string MechanicalProperty { get; set; } = string.Empty;

    [Required]
    [Column("SOLUBILITY")]
    public string Solubility { get; set; } = string.Empty;
}