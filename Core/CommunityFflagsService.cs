using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;


namespace LeitostrapV7.Core;


public class CommunityFflagEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string JsonData { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string AuthorAvatar { get; set; } = "";
    public string PreviewImage { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public int Downloads { get; set; }
    public string CreatedAt { get; set; } = "";
    public Dictionary<string, JsonElement> FFlags { get; set; } = new();
    public int FFlagCount => FFlags.Count;
}


public class CommunityFflagsService
{
    private static CommunityFflagsService? _instance;
    public static CommunityFflagsService Instance => _instance ??= new CommunityFflagsService();


    private const string SupabaseUrl = "https://docxipyxjnccykwaxqxu.supabase.co";
    private const string SupabaseKey = "sb_publishable_bUmXZdsOwvdXtSxF_A69mQ_xPpp2sRR";


    public List<CommunityFflagEntry> Entries { get; private set; } = new();
    public bool IsLoaded { get; private set; }


    private static readonly HttpClient _http = new();


    private CommunityFflagsService() { }


    public async Task LoadAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{SupabaseUrl}/rest/v1/fflags?select=*&order=created_at.desc");
            request.Headers.Add("apikey", SupabaseKey);
            request.Headers.Add("Authorization", $"Bearer {SupabaseKey}");


            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();


            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var rows = JsonSerializer.Deserialize<List<JsonElement>>(json, options) ?? new();


            Entries.Clear();
            foreach (var row in rows)
            {
                var entry = new CommunityFflagEntry
                {
                    Id = row.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Name = row.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    Description = row.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                    JsonData = row.TryGetProperty("json_data", out var jd) ? jd.GetString() ?? "" : "",
                    AuthorName = row.TryGetProperty("author_name", out var an) ? an.GetString() ?? "" : "",
                    AuthorAvatar = row.TryGetProperty("author_avatar", out var av) ? av.GetString() ?? "" : "",
                    PreviewImage = row.TryGetProperty("preview_image", out var pi) ? pi.GetString() ?? "" : "",
                    CreatedAt = row.TryGetProperty("created_at", out var ca) ? ca.GetString() ?? "" : "",
                    Downloads = row.TryGetProperty("downloads", out var dl) ? (dl.GetInt32()) : 0
                };


                if (row.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tag in tags.EnumerateArray())
                        if (tag.GetString() is string t)
                            entry.Tags.Add(t);
                }


                if (!string.IsNullOrEmpty(entry.JsonData))
                {
                    try
                    {
                        entry.FFlags = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(entry.JsonData) ?? new();
                    }
                    catch { }
                }


                if (!string.IsNullOrEmpty(entry.Name))
                    Entries.Add(entry);
            }


            IsLoaded = true;
        }
        catch
        {
            IsLoaded = false;
        }
    }
}
