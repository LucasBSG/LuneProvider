using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace LuneProvisioner.Api.IntegrationTests.Infrastructure;

public static class ApiClientExtensions
{
    public static async Task<Guid> GetSeededTemplateIdAsync(this HttpClient client)
    {
        var templates = await client.GetFromJsonAsync<List<TemplateSummary>>("/templates");
        templates.Should().NotBeNullOrEmpty();
        return templates![0].Id;
    }

    public static async Task<Guid> CreateJobAsync(
        this HttpClient client,
        Guid templateId,
        string userId,
        string environmentId,
        object parameters)
    {
        var response = await client.PostAsJsonAsync("/jobs", new
        {
            templateId,
            userId,
            environmentId,
            parameters
        });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<CreateJobResponse>();
        payload.Should().NotBeNull();
        return payload!.Id;
    }

    public static async Task<JobDetails> WaitForCompletionAsync(
        this HttpClient client,
        Guid jobId,
        TimeSpan timeout)
        => await client.WaitForStatusAsync(jobId, timeout, "Succeeded", "Failed");

    public static async Task<JobDetails> WaitForStatusAsync(
        this HttpClient client,
        Guid jobId,
        TimeSpan timeout,
        params string[] expectedStatuses)
    {
        var timeoutAt = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < timeoutAt)
        {
            var response = await client.GetAsync($"/jobs/{jobId}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var details = JsonSerializer.Deserialize<JobDetails>(
                content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            details.Should().NotBeNull();

            if (expectedStatuses.Contains(details!.Status, StringComparer.OrdinalIgnoreCase))
            {
                await Task.Delay(120);
                var finalRefresh = await client.GetAsync($"/jobs/{jobId}");
                finalRefresh.EnsureSuccessStatusCode();
                var finalContent = await finalRefresh.Content.ReadAsStringAsync();
                var hydrated = JsonSerializer.Deserialize<JobDetails>(
                    finalContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                hydrated.Should().NotBeNull();
                return hydrated!;
            }

            await Task.Delay(100);
        }

        var targets = string.Join(", ", expectedStatuses);
        throw new TimeoutException($"Job {jobId} did not reach expected statuses [{targets}] within {timeout.TotalSeconds} seconds.");
    }

    public static async Task ApproveJobAsync(this HttpClient client, Guid jobId, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/jobs/{jobId}/approve");
        request.Headers.Add("X-Lune-Token", accessToken);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
