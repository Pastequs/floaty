using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Navbar.Services
{
    public sealed record Quote(string Symbol, string Name, double Price, double Previous, string Currency, IReadOnlyList<double> Series, DateTime At)
    {
        public double Change => Price - Previous;
        public double ChangePercent => Previous == 0 ? 0 : (Price - Previous) / Previous * 100;
    }

    public sealed record SymbolHit(string Symbol, string Name, string Type, string Exchange);

    /// <summary>
    /// Cours des actions, ETF, indices et cryptomonnaies via l'API publique de Yahoo Finance (sans clé ni compte).
    /// Les cryptos s'écrivent « BTC-EUR », « ETH-USD »… comme les actions (« AAPL », « MC.PA »).
    /// Les cours sont indicatifs et peuvent être différés.
    /// </summary>
    public sealed class MarketService
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            return c;
        }

        public async Task<Quote?> GetQuoteAsync(string symbol)
        {
            try
            {
                string url = "https://query1.finance.yahoo.com/v8/finance/chart/" + Uri.EscapeDataString(symbol) + "?interval=15m&range=1d";
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
                var meta = result.GetProperty("meta");

                double price = meta.GetProperty("regularMarketPrice").GetDouble();
                double previous = meta.TryGetProperty("chartPreviousClose", out var p) ? p.GetDouble()
                                : meta.TryGetProperty("previousClose", out var pc) ? pc.GetDouble() : price;
                string currency = meta.TryGetProperty("currency", out var cur) ? cur.GetString() ?? "" : "";
                string name = meta.TryGetProperty("shortName", out var sn) ? sn.GetString() ?? symbol
                            : meta.TryGetProperty("longName", out var ln) ? ln.GetString() ?? symbol : symbol;

                var series = new List<double>();
                if (result.TryGetProperty("indicators", out var ind) && ind.GetProperty("quote")[0].TryGetProperty("close", out var closes))
                    foreach (var v in closes.EnumerateArray())
                        if (v.ValueKind == JsonValueKind.Number) series.Add(v.GetDouble());

                return new Quote(symbol, name, price, previous, currency, series, DateTime.Now);
            }
            catch { return null; }
        }

        /// <summary>Recherche d'un titre ou d'une crypto par nom ou code (« tesla », « bitcoin »).</summary>
        public async Task<List<SymbolHit>> SearchAsync(string query)
        {
            var hits = new List<SymbolHit>();
            try
            {
                string url = "https://query2.finance.yahoo.com/v1/finance/search?newsCount=0&quotesCount=8&q=" + Uri.EscapeDataString(query);
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                foreach (var q in doc.RootElement.GetProperty("quotes").EnumerateArray())
                {
                    string type = q.TryGetProperty("quoteType", out var t) ? t.GetString() ?? "" : "";
                    if (type is not ("EQUITY" or "ETF" or "CRYPTOCURRENCY" or "INDEX" or "CURRENCY" or "MUTUALFUND")) continue;

                    string symbol = q.GetProperty("symbol").GetString() ?? "";
                    string name = q.TryGetProperty("shortname", out var sn) ? sn.GetString() ?? symbol
                                : q.TryGetProperty("longname", out var ln) ? ln.GetString() ?? symbol : symbol;
                    string exchange = q.TryGetProperty("exchDisp", out var ex) ? ex.GetString() ?? "" : "";
                    if (symbol.Length > 0) hits.Add(new SymbolHit(symbol, name, type, exchange));
                }
            }
            catch { /* hors ligne ou limite atteinte : pas de résultat */ }
            return hits.Take(6).ToList();
        }

        public static string TypeLabel(string type) => type switch
        {
            "CRYPTOCURRENCY" => "Crypto",
            "ETF" => "ETF",
            "INDEX" => "Indice",
            "CURRENCY" => "Devise",
            _ => "Action",
        };

        public static string CurrencySymbol(string code) => code switch
        {
            "EUR" => "€", "USD" => "$", "GBP" => "£", "JPY" => "¥", "CHF" => "CHF", "CAD" => "CA$", _ => code,
        };

        public static string FormatPrice(double price, string currency)
        {
            string number = price >= 1000 ? price.ToString("#,0", CultureInfo.CurrentCulture)
                          : price >= 1 ? price.ToString("0.00", CultureInfo.CurrentCulture)
                          : price.ToString("0.####", CultureInfo.CurrentCulture);
            string symbol = CurrencySymbol(currency);
            return symbol is "€" or "CHF" ? $"{number} {symbol}" : $"{symbol}{number}";
        }
    }
}
