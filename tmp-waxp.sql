SELECT p."StopLossPercent", p."TakeProfitPercent", p."AverageEntryPrice", p."StopLossPrice", p."TakeProfitPrice", p."RiskPerTradePercent"
FROM "Positions" p
JOIN "Bots" b ON b."Id" = p."BotId"
WHERE p."Symbol" = 'WAXPUSDT' AND p."Quantity" > 0 AND b."DeletedAt" IS NULL;

SELECT s."TickSize", s."StepSize", s."PricePrecision", s."QuantityPrecision"
FROM "Symbols" s
WHERE s."Name" = 'WAXPUSDT' OR s."Symbol" = 'WAXPUSDT';

SELECT column_name FROM information_schema.columns WHERE table_name = 'Symbols' ORDER BY ordinal_position;
