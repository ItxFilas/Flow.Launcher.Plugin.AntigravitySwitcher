using System.Net;
using System.Net.Http;
using Flow.Launcher.Plugin.AntigravitySwitcher.Cli;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class DashboardClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }

    [Theory]
    [InlineData("[{\"id\":\"a1\"}]", true)]
    [InlineData("  {\"status\":\"ok\"}", true)]
    [InlineData("No accounts registered yet.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeJson_DetectsJsonPayloads(string? text, bool expected)
    {
        Assert.Equal(expected, DashboardClient.LooksLikeJson(text));
    }

    [Theory]
    [InlineData("{\"success\":true}", true)]
    [InlineData("{\"success\":false}", false)]
    [InlineData("{\"account_id\":\"a1\"}", false)]
    [InlineData("not json", false)]
    public void TryReadSuccess_ReadsSuccessFlag(string json, bool expected)
    {
        Assert.Equal(expected, DashboardClient.TryReadSuccess(json));
    }

    [Fact]
    public async Task LoginSwapAsync_PostsToAccountLoginSwapPath_ReturnsSuccess()
    {
        HttpRequestMessage? captured = null;
        using var client = new DashboardClient(new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"swapped\":true,\"email\":\"signed-in@example.com\"}")
            };
        }));

        var (succeeded, error) = await client.LoginSwapAsync(new Uri("http://127.0.0.1:8080/"), "acct-1");

        Assert.True(succeeded);
        Assert.Null(error);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("/api/accounts/acct-1/login-swap", captured.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "{\"error\":{\"code\":422,\"message\":\"account has no refresh token\"}}", "account has no refresh token")]
    [InlineData(HttpStatusCode.BadGateway, "{\"error\":{\"code\":502,\"message\":\"token refresh failed\",\"detail\":\"invalid token\"}}", "token refresh failed invalid token")]
    [InlineData(HttpStatusCode.InternalServerError, "not json", "The switcher returned HTTP 500.")]
    public async Task LoginSwapAsync_ReadsApiErrorBody(HttpStatusCode status, string body, string expectedError)
    {
        using var client = new DashboardClient(new StubHandler(_ =>
            new HttpResponseMessage(status) { Content = new StringContent(body) }));

        var (succeeded, error) = await client.LoginSwapAsync(new Uri("http://127.0.0.1:8080/"), "acct-1");

        Assert.False(succeeded);
        Assert.Equal(expectedError, error);
    }

    [Theory]
    [InlineData("{\"swapped\":true}")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData(null)]
    public void TryReadErrorMessage_ReturnsNullForNonErrorPayloads(string? json)
    {
        Assert.Null(DashboardClient.TryReadErrorMessage(json!));
    }
}