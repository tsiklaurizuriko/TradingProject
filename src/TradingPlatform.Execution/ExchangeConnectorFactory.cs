using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Execution;

public sealed class ExchangeConnectorFactory : IExchangeConnectorFactory
{
    private readonly ILiveExchangeConnectorFactory? _live;

    public ExchangeConnectorFactory(IEnumerable<ILiveExchangeConnectorFactory> live)
    {
        _live = live.FirstOrDefault();
    }

    public IExchangeConnector Create(TradingMode mode, Guid? exchangeAccountId)
    {
        if (mode != TradingMode.Live)
        {
            throw new DomainException(
                ErrorCodes.ValidationFailed,
                $"Trading mode {mode} is not supported. Only live mode is accepted, and this request was not mapped to live.");
        }

        return _live?.Create(exchangeAccountId)
            ?? throw new DomainException(
                ErrorCodes.LiveTradingDisabled,
                "Live trading is not wired in this process.");
    }
}
