using OqTish.Application.Common;

namespace OqTish.UnitTests.Common;

public class PageRequestTests
{
    [Fact]
    public void Defaults_to_page_one_size_twenty()
    {
        var page = PageRequest.Create(null, null);
        Assert.True(page.IsSuccess);
        Assert.Equal((1, 20, 0), (page.Value.Number, page.Value.Size, page.Value.Skip));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Out_of_range_is_validation_error(int page, int size)
    {
        var result = PageRequest.Create(page, size);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }
}
