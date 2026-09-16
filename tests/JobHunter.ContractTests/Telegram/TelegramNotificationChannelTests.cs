using System.Net;
using System.Text;
using JobHunter.Application.Notifications;
using JobHunter.Application.Security;
using JobHunter.Domain.Jobs;
using JobHunter.Notifications.Telegram;
using JobHunter.Notifications.Telegram.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.ContractTests.Telegram;

public sealed class TelegramNotificationChannelTests
{
    [Fact]
    public async Task RateLimitUsesTelegramRetryAfter()
    {
        var time = new FixedTimeProvider(DateTimeOffset.UnixEpoch);
        using var httpClient = CreateClient(
            HttpStatusCode.TooManyRequests,
            """
            {
              "ok": false,
              "error_code": 429,
              "description": "Too Many Requests",
              "parameters": { "retry_after": 7 }
            }
            """);
        var channel = CreateChannel(httpClient, time);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendOutcome.RateLimited, result.Outcome);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(7), result.RetryAtUtc);
    }

    [Fact]
    public async Task ForbiddenResponseIsPermanentFailure()
    {
        using var httpClient = CreateClient(
            HttpStatusCode.Forbidden,
            """
            {
              "ok": false,
              "error_code": 403,
              "description": "Forbidden"
            }
            """);
        var channel = CreateChannel(
            httpClient,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendOutcome.PermanentFailure, result.Outcome);
        Assert.Equal("TelegramHttp403", result.ErrorCode);
        Assert.True(result.DisableDestination);
    }

    [Fact]
    public async Task RequestTimeoutHasUnknownOutcome()
    {
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                async (_, cancellationToken) =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new InvalidOperationException("Unreachable.");
                }));
        var channel = CreateChannel(
            httpClient,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch),
            requestTimeoutSeconds: 1);

        var result = await channel.SendAsync(CreateNotification(), CancellationToken.None);

        Assert.Equal(NotificationSendOutcome.Unknown, result.Outcome);
        Assert.Equal("TelegramSendTimeout", result.ErrorCode);
    }

    [Fact]
    public void DestinationIdIsNormalizedForChannelLookup()
    {
        using var httpClient = CreateClient(
            HttpStatusCode.OK,
            """{"ok":true,"result":{}}""");
        var channel = CreateChannel(
            httpClient,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch),
            destinationId: " telegram-test ");

        Assert.Equal("telegram-test", channel.DestinationId);
    }

    private static TelegramNotificationChannel CreateChannel(
        HttpClient httpClient,
        TimeProvider timeProvider,
        int requestTimeoutSeconds = 30,
        string destinationId = "telegram-test") =>
        new(
            httpClient,
            new StubSecretReader(),
            timeProvider,
            Options.Create(
                new TelegramOptions
                {
                    Enabled = true,
                    DestinationId = destinationId,
                    RequestTimeoutSeconds = requestTimeoutSeconds
                }));

    private static HttpClient CreateClient(
        HttpStatusCode statusCode,
        string responseJson) =>
        new(
            new StubHttpMessageHandler(
                (_, _) => Task.FromResult(
                    new HttpResponseMessage(statusCode)
                    {
                        Content = new StringContent(
                            responseJson,
                            Encoding.UTF8,
                            "application/json")
                    })));

    private static JobNotification CreateNotification() =>
        new(
            "Senior .NET Engineer",
            "Example",
            ["Remote"],
            WorkplaceMode.Remote,
            90,
            "rules-only",
            "Strong match.",
            null,
            null,
            null,
            CompensationPeriod.Unknown,
            DateTimeOffset.UnixEpoch,
            "https://jobs.dou.ua/vacancies/123/");

    private sealed class StubSecretReader : ISecretReader
    {
        public string? GetSecret(string configurationKey) =>
            configurationKey switch
            {
                TelegramOptions.BotTokenConfigurationKey =>
                    "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghi",
                TelegramOptions.ChatIdConfigurationKey => "123456789",
                _ => null
            };
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
