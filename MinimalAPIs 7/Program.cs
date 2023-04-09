using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MinimalAPIs_7;
using MinimalAPIs_7.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var apiKey = "d16145386361c174778ea49e5058b789";

void ConfigureServices(IServiceCollection services)
{
    services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder().AddAuthenticationSchemes
        (JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build();
    });
}
var connectionString = "Server=127.0.0.1;Port=5432;User Id=postgres;Password=sasai123;Database=test;";
var Dbbuilder = new DbContextOptionsBuilder<MyDbContext>();
Dbbuilder.UseNpgsql(connectionString);
var options = Dbbuilder.Options;
var context = new MyDbContext(options);
builder.Services.AddDbContext<MyDbContext>(options =>
    options.UseNpgsql(connectionString));
Dictionary<int, Task> workerDictionary = new Dictionary<int, Task>();
Dictionary<int, CancellationTokenSource> cancellationTokenSourceDictionary = new Dictionary<int, CancellationTokenSource>();
ConfigureServices(builder.Services);
var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
{
    new Claim(ClaimTypes.Name, "username")
}));
builder.Services.AddEndpointsApiExplorer();
var jwtToken = "";
SymmetricSecurityKey authoKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("my_secret_key_1234567890"));
string GenerateToken(SymmetricSecurityKey key)
{
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
    issuer: "my_issuer",
    audience: "my_audience",
    claims: user.Claims,
    expires: DateTime.MaxValue,
    signingCredentials: creds
);
    jwtToken = new JwtSecurityTokenHandler().WriteToken(token);
    return jwtToken;
}

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
builder.Services.AddSwaggerGen(x =>
{
    x.AddSecurityDefinition("bearerAuth", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    });
    x.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
           new OpenApiSecurityScheme
           {
                Reference = new OpenApiReference
                    {
                       Type = ReferenceType.SecurityScheme,
                       Id = "bearerAuth"
                    }
           },
           new string[] {}
        }
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "my_issuer",
            ValidAudience = "my_audience",
            IssuerSigningKey = authoKey
        };
    });

// Добавление маршрутов

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TestService");
    }); ;
}
app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/protected", [Authorize] () =>
{
    return "Hello, protected API!";
}).RequireAuthorization();


// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

using var dbContext = new MyDbContext(options);

var users = await dbContext.Users.ToListAsync();
var cities = await dbContext.Cities.ToListAsync();
var trips = await dbContext.Trips.ToListAsync();

app.MapGet("/user/{id}", async (int id) =>
   {
       var user = users.Find(u => u.UserId == id);
       if (user is null)
           return Results.NotFound("No user found by id! :(");
       var city = await context.Cities.FindAsync(user.CityId);
       if (city is null)
           return Results.NotFound("No city found for user!");

       var weather = await GetWeatherAsync(city.Latitude, city.Longitude, apiKey);
       if (weather is null)
           return Results.NotFound("Could not retrieve weather information!");

       var result = new
       {
           user,
           weather
       };
       return Results.Ok(result);

   }).RequireAuthorization();

app.MapGet("/token", (HttpContext httpContext) =>
{
    return Results.Ok(GenerateToken(authoKey));

}).AllowAnonymous();
app.MapGet("users/lat={lat}&lon={lon}&r={r}", (float lat, float lon, float r) =>
{
    List<City> citiesInRadius = GetCitiesInRadius(lat, lon, r, cities);

    List<User> usersInRadius = users
        .Where(u => citiesInRadius.Any(c => c.CityId == u.CityId))
        .ToList();

    return Results.Ok(usersInRadius);
}).WithMetadata(new EndpointNameMetadata("GetUsers"));
app.MapPost("/cities", async (City city) =>
{
    city.CityId = 0;
    dbContext.Cities.Add(city);
    await dbContext.SaveChangesAsync();
    return Results.Ok(city);
});
app.MapPost("/users", async (User user) =>
{
    if (user.Name.Length > 2)
    {
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return Results.Ok(user);
    }
    else
        return Results.BadRequest("Name can't be shorter than 2 symbols :(");
});

app.MapPost("/trips", async (MyDbContext dbContext, TripRequest request) =>
{
    var user = await dbContext.Users.FindAsync(request.UserId);
    if (user == null)
    {
        return Results.NotFound();
    }
    if (context.Trips.Any(t => t.UserId == user.UserId && !t.IsCanceled))
    {
        return Results.BadRequest("User is already in transit.");
    }
    var trip = new Trip
    {
        UserId = request.UserId,
        DestinationCityId = request.DestinationCityId,
        TripTime = request.TripTime,
        IsCanceled = false,
        Token = Guid.NewGuid().ToString(),
        CreateTime = DateTime.Now,
    };
    await dbContext.Trips.AddAsync(trip);
    await dbContext.SaveChangesAsync();

    // запрещаем создавать новый трип, если пользователь уже в переезде


    // запускаем фоновый воркер
    var tripId = trip.TripId;
    var cancellationTokenString = trip.Token;
    var worker = Task.Run(async () =>
    {
        for (int i = 0; i < trip.TripTime; i += 5)
        {
            Console.WriteLine($"UserId: {request.UserId}, TripId: {tripId}");
            await Task.Delay(TimeSpan.FromSeconds(5));
        }

    });
    user.CityId = request.DestinationCityId;
    await dbContext.SaveChangesAsync();
    trip.TripId = tripId;

    workerDictionary[tripId] = worker;
    TripResponse response = new TripResponse
    {
        TripId = trip.TripId,
        Token = trip.Token
    };
    return Results.Created($"/trips/{tripId}", new { TripId = tripId, Token = trip.Token });
});
app.MapGet("/trip/{id}", (int id) =>
{
    var time = 0;
    var trip = trips.Find(u => u.TripId == id);
    if (trip is null)
        return Results.NotFound("No trip found by id! :(");
    if (trip.IsCanceled)
        return Results.Ok("Trip is canceled");
    if ((DateTime.Now - trip.CreateTime).TotalSeconds > trip.TripTime)
        return Results.Ok("The trip is over");
    if ((DateTime.Now - trip.CreateTime).TotalSeconds < trip.TripTime)
        time = Convert.ToInt32(trip.TripTime - (DateTime.Now - trip.CreateTime).TotalSeconds);
    return Results.Ok(time.ToString());

}).RequireAuthorization();
app.MapPost("/trips/cancellation-token={cancellationToken}", async (string cancellationToken) =>
{
    var trip = trips.Find(t => t.Token == cancellationToken);
    if (trip is null)
        return Results.NotFound("No trip found by id! :(");
    if (trip.IsCanceled == true)
        return Results.BadRequest("Trip is already canceled!");
    trip.IsCanceled = true;
    workerDictionary[trip.TripId].Dispose();
    return Results.Ok("Trip is canceled!");
});


async Task<Weather> GetWeatherAsync(double lat, double lon, string key)
{
    using var httpClient = new HttpClient();
    var response = await httpClient.GetAsync($"https://api.openweathermap.org/data/2.5/weather?lat={lat}&lon={lon}&appid={apiKey}&units=metric");
    if (response.IsSuccessStatusCode)
    {
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        var weather = new Weather
        {
            Description = result.GetProperty("weather")[0].GetProperty("description").GetString(),
            Temperature = result.GetProperty("main").GetProperty("temp").GetDouble()
        };
        return weather;
    }
    return null;
}


List<City> GetCitiesInRadius(double lat, double lon, double radius, List<City> cities)
{
    const double earthRadius = 6371; // радиус Земли в км

    List<City> citiesInRadius = new List<City>();

    foreach (City city in cities)
    {
        double dLat = (city.Latitude - lat) * Math.PI / 180.0;
        double dLon = (city.Longitude - lon) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat * Math.PI / 180.0) * Math.Cos(city.Latitude * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        double distance = earthRadius * c;

        if (distance <= radius)
        {
            citiesInRadius.Add(city);
        }
    }

    return citiesInRadius;
}


app.Run();
