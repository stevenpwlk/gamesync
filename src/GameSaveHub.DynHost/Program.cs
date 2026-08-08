using System.Net.Http.Headers;
using System.Text;

var hostname = RequireEnvironment("DYNHOST_HOSTNAME");
var username = ReadSecret("DYNHOST_USERNAME_FILE");
var password = ReadSecret("DYNHOST_PASSWORD_FILE");
var endpoint = Environment.GetEnvironmentVariable("DYNHOST_ENDPOINT") ?? "https://dns.eu.ovhapis.com/nic/update";
var intervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("DYNHOST_INTERVAL_SECONDS"), out var configuredInterval)
    ? configuredInterval
    : 300;
if (intervalSeconds is < 60 or > 86400) throw new InvalidOperationException("DYNHOST_INTERVAL_SECONDS doit être compris entre 60 et 86400.");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password)));
http.DefaultRequestHeaders.UserAgent.ParseAdd("GameSaveHub-DynHost/1.0");

using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
do
{
    try
    {
        var uri = $"{endpoint}?system=dyndns&hostname={Uri.EscapeDataString(hostname)}";
        using var response = await http.GetAsync(uri);
        var result = (await response.Content.ReadAsStringAsync()).Trim();
        if (!response.IsSuccessStatusCode || !(result.StartsWith("good", StringComparison.OrdinalIgnoreCase) || result.StartsWith("nochg", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"{DateTimeOffset.UtcNow:O} DynHost refusé: HTTP {(int)response.StatusCode}, réponse {Sanitize(result)}");
        }
        else
        {
            Console.WriteLine($"{DateTimeOffset.UtcNow:O} DynHost {hostname}: {Sanitize(result)}");
        }
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
    {
        Console.Error.WriteLine($"{DateTimeOffset.UtcNow:O} DynHost indisponible: {exception.GetType().Name}");
    }
}
while (await timer.WaitForNextTickAsync());

static string RequireEnvironment(string name)
{
    var value = Environment.GetEnvironmentVariable(name)?.Trim();
    return string.IsNullOrEmpty(value) ? throw new InvalidOperationException($"Variable {name} absente.") : value;
}

static string ReadSecret(string variable)
{
    var path = RequireEnvironment(variable);
    var value = File.ReadAllText(path).Trim();
    return value.Length == 0 ? throw new InvalidOperationException($"Secret vide : {variable}.") : value;
}

static string Sanitize(string value)
{
    var singleLine = value.Replace('\r', ' ').Replace('\n', ' ');
    return singleLine[..Math.Min(singleLine.Length, 160)];
}
