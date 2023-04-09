namespace MinimalAPIs_7
{
    public class Weather
    {
        public required string Description { get; set; }
        public double Temperature { get; set; }
    }

    public class TripRequest
    {
        public int UserId { get; set; }
        public int DestinationCityId { get; set; }
        public int TripTime { get; set; }
    }
    public class TripResponse
    {
        public int TripId { get; set; }
        public required string Token { get; set; }

    }
}
