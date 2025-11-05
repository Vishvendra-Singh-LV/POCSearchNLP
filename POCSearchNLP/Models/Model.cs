using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace POCSearchNLP.Models
{
    public class Model
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ModelID { get; set; }

        [Required]
        public int MakeID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public short? YearFrom { get; set; }

        public short? YearTo { get; set; }

        [StringLength(50)]
        public string? BodyStyle { get; set; }

        // Navigation properties
        [ForeignKey("MakeID")]
        public virtual Make Make { get; set; } = null!;

        public virtual ICollection<PartsInfo> PartsInfo { get; set; } = new List<PartsInfo>();
    }
}