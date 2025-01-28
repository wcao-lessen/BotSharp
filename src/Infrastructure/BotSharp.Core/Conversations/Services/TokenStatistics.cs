using BotSharp.Abstraction.Conversations.Enums;
using BotSharp.Abstraction.MLTasks;
using System.Diagnostics;

namespace BotSharp.Core.Conversations.Services;

public class TokenStatistics : ITokenStatistics
{
    private int _promptTokenCount = 0;
    private float _promptCost = 0f;
    private int _completionTokenCount = 0;
    private float _completionCost = 0f;
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    public int Total => _promptTokenCount + _completionTokenCount;
    public string _model;
    private Stopwatch _timer;

    public float Cost => _promptCost + _completionCost;
    public float AccumulatedCost
    {
        get 
        {
            var stat = _services.GetRequiredService<IConversationStateService>();
            return float.Parse(stat.GetState("llm_total_cost", "0"));
        }
    }

    public TokenStatistics(IServiceProvider services, ILogger<TokenStatistics> logger) 
    { 
        _services = services;
        _logger = logger;
    }

    public void AddToken(TokenStatsModel stats, RoleDialogModel message)
    {
        _model = stats.Model;
        _promptTokenCount += stats.PromptCount;
        _completionTokenCount += stats.CompletionCount;

        var settingsService = _services.GetRequiredService<ILlmProviderService>();
        var settings = settingsService.GetSetting(stats.Provider, _model);

        var deltaPromptCost = stats.PromptCount / 1000f * settings.PromptCost;
        var deltaCompletionCost = stats.CompletionCount / 1000 * settings.CompletionCost;
        _promptCost += deltaPromptCost;
        _completionCost += deltaCompletionCost;

        // Accumulated Token
        var stat = _services.GetRequiredService<IConversationStateService>();
        var inputCount = int.Parse(stat.GetState("prompt_total", "0"));
        stat.SetState("prompt_total", stats.PromptCount + inputCount, isNeedVersion: false, source: StateSource.Application);
        var outputCount = int.Parse(stat.GetState("completion_total", "0"));
        stat.SetState("completion_total", stats.CompletionCount + outputCount, isNeedVersion: false, source: StateSource.Application);

        // Total cost
        var total_cost = float.Parse(stat.GetState("llm_total_cost", "0"));
        total_cost += Cost;
        stat.SetState("llm_total_cost", total_cost, isNeedVersion: false, source: StateSource.Application);


        var globalStats = _services.GetRequiredService<IBotSharpStatsService>();
        var body = new BotSharpStatsInput
        {
            Metric = StatsCategory.AgentLlmCost,
            Dimension = message.CurrentAgentId,
            RecordTime = DateTime.UtcNow,
            Data = [
                new StatsKeyValuePair("prompt_token_count_total", stats.PromptCount),
                new StatsKeyValuePair("completion_token_count_total", stats.CompletionCount),
                new StatsKeyValuePair("prompt_cost_total", deltaPromptCost),
                new StatsKeyValuePair("completion_cost_total", deltaCompletionCost)
            ]
        };
        globalStats.UpdateStats("global-llm-cost", body);
    }

    public void PrintStatistics()
    {
        if (_timer == null)
        {
            _timer = Stopwatch.StartNew();
        }
        else
        {
            _timer.Start();
        }
        var stats = $"Token Usage: {_promptTokenCount} prompt + {_completionTokenCount} completion = {Total} total tokens ({_timer.ElapsedMilliseconds / 1000f:f2}s). One-Way cost: {Cost:C4}, accumulated cost: {AccumulatedCost:C4}. [{_model}]";
#if DEBUG
        Console.WriteLine(stats);
#else
        _logger.LogInformation(stats);
#endif
    }

    public void StartTimer()
    {
        if (_timer == null)
        {
            _timer = Stopwatch.StartNew();
        }
        else
        {
            _timer.Start();
        }
    }

    public void StopTimer()
    {
        _timer.Stop();
    }
}
