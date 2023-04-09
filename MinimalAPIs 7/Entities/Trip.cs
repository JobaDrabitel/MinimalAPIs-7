using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace MinimalAPIs_7.Entities
{
    public class Trip
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]

        public int TripId { get; set; }
        public int UserId { get; set; }
        public int DestinationCityId { get; set; }
        public int TripTime { get; set; }
        public required string Token { get; set; }
        public bool IsCanceled { get; set; }
        public DateTime CreateTime { get; set; }

    }
}
