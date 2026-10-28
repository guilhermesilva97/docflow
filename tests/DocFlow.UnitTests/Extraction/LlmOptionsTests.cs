using DocFlow.Infrastructure.Extraction;
using OpenAI.Chat;

namespace DocFlow.UnitTests.Extraction;

public class LlmOptionsTests
{
    [Fact]
    public void Omits_temperature_and_reasoning_effort_by_default()
    {
        var options = OpenAIInvoiceExtractor.BuildOptions(new LlmOptions());

        Assert.Null(options.Temperature);
        Assert.Null(options.ReasoningEffortLevel);
    }

    [Fact]
    public void Sends_configured_temperature_and_reasoning_effort()
    {
        var options = OpenAIInvoiceExtractor.BuildOptions(new LlmOptions { Temperature = 0, ReasoningEffort = "low" });

        Assert.Equal(0f, options.Temperature);
        Assert.Equal(ChatReasoningEffortLevel.Low, options.ReasoningEffortLevel);
    }
}
