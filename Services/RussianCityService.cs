using Newtonsoft.Json;

namespace MeetAndGreet.API.Services
{
    public class RussianCityService
    {
        private const string DataUrl = "https://raw.githubusercontent.com/pensnarik/russian-cities/master/russian-cities.json";
        private readonly HttpClient _httpClient;
        private readonly ILogger<RussianCityService> _logger;
        private readonly RedisService _redisService;

        public RussianCityService(HttpClient httpClient, ILogger<RussianCityService> logger, RedisService redisService)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _logger.LogInformation("RussianCityService constructed.");
        }

        public async Task<List<CityDto>> GetCitiesAsync()
        {
            _logger.LogInformation("Getting Russian cities.");
            try
            {
                const string cacheKey = "russian_cities";
                
                var cachedCitiesJson = await _redisService.GetValueAsync(cacheKey);

                if (!string.IsNullOrEmpty(cachedCitiesJson))
                {
                    _logger.LogDebug("Cities found in Redis.");
                    var cachedCities = JsonConvert.DeserializeObject<List<CityDto>>(cachedCitiesJson);
                    return cachedCities ?? new List<CityDto>();
                }

                _logger.LogDebug("Cities not found in Redis. Fetching from URL.");
                var response = await _httpClient.GetFromJsonAsync<List<CityData>>(DataUrl);

                if (response == null)
                {
                    _logger.LogWarning("Failed to retrieve cities from URL. Response is null.");
                    return new List<CityDto>();
                }

                var cities = response
                    .Where(x => !string.IsNullOrEmpty(x.Name))
                    .Select(x => new CityDto(
                        Name: x.Name,
                        Region: x.Region ?? x.FederalDistrict ?? "Не указано"
                    ))
                    .ToList();
                
                var citiesJson = JsonConvert.SerializeObject(cities);
                
                await _redisService.SetValueAsync(cacheKey, citiesJson);
                _logger.LogInformation("Cities cached for 1 day in Redis.");
                return cities;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request error while getting cities.");
                return new List<CityDto>();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization error while getting cities.");
                return new List<CityDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cities.");
                return new List<CityDto>();
            }
            finally
            {
                _logger.LogInformation("GetCitiesAsync finished.");
            }
        }

        private class CityData
        {
            public string Name { get; set; }
            public string Region { get; set; }
            public string FederalDistrict { get; set; }
        }

        public record CityDto(string Name, string Region);
    }
}