module PortfolioOptimization.DataLoader

open System
open System.IO
open System.Globalization
open PortfolioOptimization.Types

let dowJonesTickers : Tickers =
    [|
        "AAPL"; "AMGN"; "AMZN"; "AXP";  "BA";   "CAT";  "CRM";  "CSCO"; "CVX";  "DIS"
        "GS";   "HD";   "HON";  "IBM";  "JNJ";  "JPM";  "KO";   "MCD";  "MMM";  "MRK"
        "MSFT"; "NKE";  "NVDA"; "PG";   "SHW";  "TRV";  "UNH";  "V";    "VZ";   "WMT"
    |]

let parseReturnsCsv (csv: string) : Tickers * ReturnsMatrix =
    let lines =
        csv.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
    let header = lines.[0].Split(',') |> Array.map (fun s -> s.Trim())
    let rows =
        lines
        |> Array.skip 1
        |> Array.map (fun line ->
            line.Split(',')
            |> Array.map (fun s ->
                Double.Parse(s.Trim(), CultureInfo.InvariantCulture)))
    header, rows

let loadReturnsFromCsv (path: string) : Tickers * ReturnsMatrix =
    let text = File.ReadAllText(path)
    parseReturnsCsv text
