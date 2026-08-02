using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace RPGWeb.E2ETests;

[Collection(WebAppCollection.Name)]
public sealed class BrowserPersistenceTests : BrowserTest
{
    private readonly WebAppFixture _webApp;

    public BrowserPersistenceTests(WebAppFixture webApp)
    {
        _webApp = webApp;
    }

    [Fact]
    public async Task SavesSurviveRefreshAndRemainIsolatedBetweenBrowsers()
    {
        var firstContext = await Browser.NewContextAsync();
        var secondContext = await Browser.NewContextAsync();

        try
        {
            var firstPage = await firstContext.NewPageAsync();
            firstPage.SetDefaultTimeout(15_000);

            await firstPage.GotoAsync(_webApp.BaseUrl);
            await WaitForInteractiveCircuitAsync(firstPage);
            await Expect(firstPage.GetByTestId("new-game-fantasy_quest")).ToBeVisibleAsync();
            await Expect(firstPage.GetByTestId("continue-game-fantasy_quest")).ToHaveCountAsync(0);

            await firstPage.GetByTestId("new-game-fantasy_quest").ClickAsync();
            await Expect(firstPage).ToHaveURLAsync(new Regex("/play$"));
            await Expect(firstPage.GetByTestId("current-room")).ToHaveTextAsync("Ravensholm Town Square");
            await Expect(firstPage.GetByTestId("turn-summary")).ToContainTextAsync("Turn 0");

            await SubmitCommandAsync(firstPage, "go north");
            await Expect(firstPage.GetByTestId("current-room")).ToHaveTextAsync("Forest Entrance");
            await Expect(firstPage.GetByTestId("turn-summary")).ToContainTextAsync("Turn 1");
            await Expect(firstPage.GetByTestId("save-status")).ToContainTextAsync("Saved on turn 1");

            await firstPage.ReloadAsync();
            await WaitForInteractiveCircuitAsync(firstPage);
            await Expect(firstPage.GetByText("No game active.")).ToBeVisibleAsync();
            await firstPage.GetByRole(AriaRole.Button, new() { Name = "Select a Game" }).ClickAsync();
            await firstPage.GetByTestId("continue-game-fantasy_quest").ClickAsync();
            await Expect(firstPage.GetByTestId("current-room")).ToHaveTextAsync("Forest Entrance");
            await Expect(firstPage.GetByTestId("turn-summary")).ToContainTextAsync("Turn 1");

            var secondPage = await secondContext.NewPageAsync();
            secondPage.SetDefaultTimeout(15_000);
            await secondPage.GotoAsync(_webApp.BaseUrl);
            await WaitForInteractiveCircuitAsync(secondPage);
            await Expect(secondPage.GetByTestId("continue-game-fantasy_quest")).ToHaveCountAsync(0);
            await secondPage.GetByTestId("new-game-fantasy_quest").ClickAsync();
            await SubmitCommandAsync(secondPage, "go east");
            await Expect(secondPage.GetByTestId("current-room")).ToHaveTextAsync("The Wandering Wyvern Tavern");
            await Expect(secondPage.GetByTestId("save-status")).ToContainTextAsync("Saved on turn 1");

            await firstPage.ReloadAsync();
            await WaitForInteractiveCircuitAsync(firstPage);
            await firstPage.GetByRole(AriaRole.Button, new() { Name = "Select a Game" }).ClickAsync();
            await firstPage.GetByTestId("continue-game-fantasy_quest").ClickAsync();
            await Expect(firstPage.GetByTestId("current-room")).ToHaveTextAsync("Forest Entrance");
            await Expect(firstPage.GetByTestId("turn-summary")).ToContainTextAsync("Turn 1");
        }
        finally
        {
            await firstContext.CloseAsync();
            await secondContext.CloseAsync();
        }
    }

    private static async Task SubmitCommandAsync(IPage page, string command)
    {
        await page.GetByTestId("command-input").FillAsync(command);
        await page.GetByTestId("command-submit").ClickAsync();
    }

    private static Task WaitForInteractiveCircuitAsync(IPage page) =>
        page.GetByTestId("interactive-ready").WaitForAsync(new()
        {
            State = WaitForSelectorState.Attached
        });
}
