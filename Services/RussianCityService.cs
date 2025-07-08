using Microsoft.Extensions.Caching.Memory;

namespace MeetAndGreet.API.Services
{
    public class RussianCityService
    {
        private const string DataUrl = "https://raw.githubusercontent.com/pensnarik/russian-cities/master/russian-cities.json";

        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;

        public RussianCityService(HttpClient httpClient, IMemoryCache cache)
        {
            _httpClient = httpClient;
            _cache = cache;
        }

        public async Task<List<CityDto>> GetCitiesAsync()
        {
            if (_cache.TryGetValue("russian_cities", out List<CityDto> cachedCities))
                return cachedCities;

            var response = await _httpClient.GetFromJsonAsync<List<CityData>>(DataUrl);

            var cities = response
                .Where(x => !string.IsNullOrEmpty(x.Name))
                .Select(x => new CityDto(
                    Name: x.Name,
                    Region: x.Region ?? x.FederalDistrict ?? "Не указано"
                ))
                .ToList();

            _cache.Set("russian_cities", cities, TimeSpan.FromDays(1));
            return cities;
        }

        private class CityData
        {
            public string Name { get; set; }
            public string Region { get; set; }
            public string FederalDistrict { get; set; }
        }
    }

    public record CityDto(string Name, string Region);
}
