using System.Net;
using System.Text;
using JobHunter.Application.Notifications;
using JobHunter.Application.Security;
using JobHunter.Notifications.Telegram;
using JobHunter.Notifications.Telegram.Configuration;
using Microsoft.Extensions.Options;

namespace JobHunter.ContractTests.Telegram;

public sealed class TelegramSetupServiceTests
{
    [Fact]
    public async Task ValidateChecksPrivateChatSendsMessageAndEnablesDestination()
    {
        var responses = new Queue<HttpResponseMessage>(
            [
                JsonResponse(
                    """
                    {
                      "ok": true,
                      "result": {
                        "id": 987654321,
                        "is_bot": true,
                        "first_name": "Job Hunter",
                        "username": "job_hunter_test_bot"
                      }
                    }
                    """),
                JsonResponse(
                    """
                    {
                      "ok": true,
                      "result": {
                        "id": 123456789,
                        "type": "private",
                        "first_name": "Test"
                      }
                    }
                    """),
                JsonResponse(
                    """
                    {
                      "ok": true,
                      "result": {
                        "message_id": 42,
                        "date": 1789552800,
                        "chat": {
                          "id": 123456789,
                          "type": "private",
                          "first_name": "Test"
                        }
                      }
                    }
                    """)
            ]);
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                (_, _) => Task.FromResult(responses.Dequeue())));
        var destinationStore = new StubDestinationStateStore();
        var service = CreateService(httpClient, destinationStore);

        var result = await service.ValidateAsync(CancellationToken.None);

        Assert.Equal("job_hunter_test_bot", result.BotUsername);
        Assert.True(destinationStore.Enabled);
        Assert.Equal("telegram-test", destinationStore.DestinationId);
        Assert.Empty(responses);
    }

    [Fact]
    public async Task CheckValidatesPrivateChatWithoutSendingOrChangingDestination()
    {
        var responses = new Queue<HttpResponseMessage>(
            [
                JsonResponse(
                    """
                    {
                      "ok": true,
                      "result": {
                        "id": 987654321,
                        "is_bot": true,
                        "first_name": "Job Hunter",
                        "username": "job_hunter_test_bot"
                      }
                    }
                    """),
                JsonResponse(
                    """
                    {
                      "ok": true,
                      "result": {
                        "id": 123456789,
                        "type": "private",
                        "first_name": "Test"
                      }
                    }
                    """)
            ]);
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                (_, _) => Task.FromResult(responses.Dequeue())));
        var destinationStore = new StubDestinationStateStore();
        var service = CreateService(httpClient, destinationStore);

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal("job_hunter_test_bot", result.BotUsername);
        Assert.False(destinationStore.Enabled);
        Assert.Null(destinationStore.DestinationId);
        Assert.Empty(responses);
    }

    [Fact]
    public async Task ValidateRejectsNonPrivateDestination()
    {
        var responses = new Queue<HttpResponseMessage>(
            [
                JsonResponse(
                    """
                    {
                      "ok": true,
                      "result": {
                        "id": 987654321,
                        "is_bot": true,
                        "first_name": "Job Hunter"
                      }
                    }
                    """),
                JsonResponse(
                    """
                    {
                      "ok": true,
                      "result": {
                        "id": -100123456789,
                        "type": "supergroup",
                        "title": "Not private"
                      }
                    }
                    """)
            ]);
        using var httpClient = new HttpClient(
            new StubHttpMessageHandler(
                (_, _) => Task.FromResult(responses.Dequeue())));
        var destinationStore = new StubDestinationStateStore();
        var service = CreateService(httpClient, destinationStore);

        var exception = await Assert.ThrowsAsync<TelegramSetupException>(
            () => service.ValidateAsync(CancellationToken.None));

        Assert.Contains("private chat", exception.Message, StringComparison.Ordinal);
        Assert.False(destinationStore.Enabled);
    }

    private static TelegramSetupService CreateService(
        HttpClient httpClient,
        INotificationDestinationStateStore destinationStateStore) =>
        new(
            httpClient,
            new StubSecretReader(),
            destinationStateStore,
            TimeProvider.System,
            Options.Create(
                new TelegramOptions
                {
                    DestinationId = "telegram-test",
                    RequestTimeoutSeconds = 30
                }));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

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

    private sealed class StubDestinationStateStore
        : INotificationDestinationStateStore
    {
        public bool Enabled { get; private set; }

        public string? DestinationId { get; private set; }

        public Task<NotificationDestinationStateSnapshot?> GetAsync(
            string destinationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetDestinationEnabledAsync(
            string destinationId,
            bool enabled,
            string? failureCode,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DestinationId = destinationId;
            Enabled = enabled;
            return Task.CompletedTask;
        }
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
}
