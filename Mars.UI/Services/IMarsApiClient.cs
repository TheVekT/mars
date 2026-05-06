using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Mars.UI.Models;

namespace Mars.UI.Services;

public interface IMarsApiClient
{
    Task<bool> AuthenticateAsync(string ip, string port, string token);
    Task<List<HttpModuleSchema>> GetSchemaAsync(string ip, string port, string token);
    Task<List<WsModuleSchema>> GetWsSchemaAsync(string ip, string port, string token);
    Task<string> FetchTextAsync(string ip, string port, string token, string endpoint);
    Task<string> PostJsonAsync(string ip, string port, string token, string endpoint, string jsonContent);
}
