namespace Drift.Tests;

public class WebViewPartsTests
{
    [Theory]
    [InlineData("dgiklkfkllikcanfonkcabmbdfmgleag")] // Microsoft Clipboard Extension
    [InlineData("mhjfbmdgcfjbbpaeojofohoefgiehjai")] // Microsoft Edge PDF Viewer
    [InlineData("kfbdpdaobnofkbopebjglnaadopfikhh")] // Microsoft Edge DevTools Enhancements, added when DevTools first opens
    [InlineData("cgjgjfacjflmgphhhepmbhhbgjieaecn")] // Microsoft Edge Unminification Extension, likewise
    public void WebView2s_own_extensions_are_not_listed_as_yours(string id) =>
        Assert.True(WebViewParts.IsBuiltIn(id));

    [Fact]
    public void An_extension_you_added_is_yours() =>
        Assert.False(WebViewParts.IsBuiltIn("bgckgiehdecfdoahbaaokgdhbmbmfgba"));
}
