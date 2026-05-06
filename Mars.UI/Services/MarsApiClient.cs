using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Mars.UI.Models;

namespace Mars.UI.Services;

public class MarsApiClient : IMarsApiClient
{
    private readonly HttpClient _httpClient;

    public MarsApiClient()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<bool> AuthenticateAsync(string ip, string port, string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://{ip}:{port}/api/v1/auth");
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Add("X-MARS-Auth", token);
            }

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return false; // Unauthorized implies wrong or missing token
            }
            
            throw new Exception($"Server returned error: {response.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Connection error: {ex.Message}", ex);
        }
        catch (TaskCanceledException)
        {
            throw new Exception("Connection timed out.");
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string ip, string port, string token, string endpoint)
    {
        string url = endpoint;
        if (!endpoint.StartsWith("http"))
        {
            string basePath = endpoint.StartsWith("/api/v1") ? endpoint : $"/api/v1{endpoint}";
            url = $"http://{ip}:{port}{basePath}";
        }

        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Add("X-MARS-Auth", token);
        }
        return request;
    }

    public async Task<List<HttpModuleSchema>> GetSchemaAsync(string ip, string port, string token)
    {
        var request = CreateRequest(HttpMethod.Get, ip, port, token, "/schema");
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var jsonStr = await response.Content.ReadAsStringAsync();
        var result = System.Text.Json.JsonSerializer.Deserialize<SchemaResponse>(jsonStr);
        return result?.Schema ?? new List<HttpModuleSchema>();
    }

    public async Task<List<WsModuleSchema>> GetWsSchemaAsync(string ip, string port, string token)
    {
        var request = CreateRequest(HttpMethod.Get, ip, port, token, "/ws_schema");
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var jsonStr = await response.Content.ReadAsStringAsync();
        var result = System.Text.Json.JsonSerializer.Deserialize<WsSchemaResponse>(jsonStr);
        return result?.Schema ?? new List<WsModuleSchema>();
    }

    public async Task<string> FetchTextAsync(string ip, string port, string token, string endpoint)
    {
        var request = CreateRequest(HttpMethod.Get, ip, port, token, endpoint);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> PostJsonAsync(string ip, string port, string token, string endpoint, string jsonContent)
    {
        var request = CreateRequest(HttpMethod.Post, ip, port, token, endpoint);
        request.Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
