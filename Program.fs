module PortfolioOptimization.Program

open System
open System.Diagnostics
open System.Globalization
open PortfolioOptimization.Types
open PortfolioOptimization.DataLoader
open PortfolioOptimization.Portfolio
open PortfolioOptimization.Simulate

let parseArgs (argv: string[]) : Map<string, string> =
    argv
    |> Array.choose (fun a ->
        if a.StartsWith("--") then
            let stripped = a.Substring(2)
            match stripped.IndexOf('=') with
            | -1 -> Some (stripped, "true")
            | i  -> Some (stripped.Substring(0, i), stripped.Substring(i + 1))
        else None)
    |> Map.ofArray

let argString (args: Map<string,string>) (key: string) (def: string) =
    match Map.tryFind key args with Some v -> v | None -> def

let argInt (args: Map<string,string>) (key: string) (def: int) =
    match Map.tryFind key args with
    | Some v -> Int32.Parse(v, CultureInfo.InvariantCulture)
    | None -> def

let argFloat (args: Map<string,string>) (key: string) (def: float) =
    match Map.tryFind key args with
    | Some v -> Double.Parse(v, CultureInfo.InvariantCulture)
    | None -> def

let argBool (args: Map<string,string>) (key: string) (def: bool) =
    match Map.tryFind key args with
    | Some v -> v = "true" || v = "1" || v = "yes"
    | None -> def

let printBanner (title: string) =
    let line = String.replicate 70 "="
    printfn ""
    printfn "+%s+" line
    printfn "| %-68s |" title
    printfn "+%s+" line

let printPortfolio (tickers: Tickers) (p: EvaluatedPortfolio) =
    printfn ""
    printfn "  ================================================================"
    printfn "  MELHOR CARTEIRA ENCONTRADA"
    printfn "  ================================================================"
    printfn "  Retorno anualizado    :  %7.2f%%" (p.AnnualReturn * 100.0)
    printfn "  Volatilidade anualiz. :  %7.2f%%" (p.AnnualVolatility * 100.0)
    printfn "  Sharpe Ratio          :  %7.4f" p.Sharpe
    printfn ""
    printfn "  Ativos (%d) e pesos:" p.AssetIndices.Length
    let pairs =
        Array.zip p.AssetIndices p.Weights
        |> Array.sortByDescending snd
    for (idx, w) in pairs do
        let bar = String.replicate (int (w * 100.0)) "#"
        printfn "    %-6s %6.2f%%  %s" tickers.[idx] (w * 100.0) bar
    let sum = Array.sum p.Weights
    printfn ""
    printfn "  Soma dos pesos        :  %.4f" sum
    printfn "  ================================================================"

let runSimulation
    (label: string)
    (allTickers: Tickers)
    (allReturns: ReturnsMatrix)
    (config: SimConfig)
    : EvaluatedPortfolio * TimeSpan =

    let totalCombos =
        let n = allTickers.Length
        let k = min config.K (n - config.K)
        let mutable result = 1L
        for i in 1 .. k do
            result <- result * int64 (n - i + 1) / int64 i
        result

    let combosToRun =
        if config.MaxCombos < 0 then totalCombos
        else min (int64 config.MaxCombos) totalCombos

    printfn ""
    printfn "Configuracao [%s]:" label
    printfn "  Modo paralelo      : %b" config.Parallel
    printfn "  K (ativos/carteira): %d" config.K
    printfn "  Max combinacoes    : %d  (de C(%d,%d) = %d)"
        (int combosToRun) allTickers.Length config.K (int totalCombos)
    printfn "  Sims/combinacao    : %s" (config.SimsPerCombo.ToString("N0"))
    printfn "  Seed base          : %d" config.Seed
    printfn "  Cap por ativo      : %.2f" config.MaxWeight
    printfn "  Cores disponiveis  : %d" Environment.ProcessorCount
    printfn "  Total de simulacoes: %s"
        ((int64 config.SimsPerCombo * combosToRun).ToString("N0"))
    printfn ""
    printfn "Rodando..."
    let sw = Stopwatch.StartNew()
    let best = findBestPortfolio allReturns allTickers.Length config
    sw.Stop()
    printfn "Tempo total: %.2f s" sw.Elapsed.TotalSeconds

    printPortfolio allTickers best
    best, sw.Elapsed

let runBenchmark
    (allTickers: Tickers)
    (allReturns: ReturnsMatrix)
    (config: SimConfig)
    (runs: int)
    : unit =

    printBanner (sprintf "BENCHMARK: paralelo vs sequencial (%d rodadas cada)" runs)

    let totalSims = int64 config.MaxCombos * int64 config.SimsPerCombo
    printfn ""
    printfn "Configuracao do benchmark:"
    printfn "  Combinacoes/rodada : %d" config.MaxCombos
    printfn "  Sims/combinacao    : %d" config.SimsPerCombo
    printfn "  Total de sims/rodada: %s" (totalSims.ToString("N0"))
    printfn "  Rodadas por modo   : %d" runs
    printfn "  Cores disponiveis  : %d" Environment.ProcessorCount
    printfn ""

    let runOnce (parallelMode: bool) : float * float =
        let cfg = { config with Parallel = parallelMode }
        let sw = Stopwatch.StartNew()
        let best = findBestPortfolio allReturns allTickers.Length cfg
        sw.Stop()
        sw.Elapsed.TotalSeconds, best.Sharpe

    let benchMode (label: string) (parallelMode: bool) : float[] * float =
        printfn "--- Modo: %s ---" label
        let times = ResizeArray<float>()
        let mutable lastSharpe = 0.0
        for r in 1 .. runs do
            let t, sr = runOnce parallelMode
            times.Add(t)
            lastSharpe <- sr
            printfn "  Rodada %d: %.3f s  (Sharpe = %.4f)" r t sr
        printfn ""
        times.ToArray(), lastSharpe

    let parTimes, parSharpe = benchMode "PARALELO" true
    let seqTimes, seqSharpe = benchMode "SEQUENCIAL" false

    let stats (xs: float[]) =
        Array.average xs, Array.min xs, Array.max xs

    let parAvg, parMin, parMax = stats parTimes
    let seqAvg, seqMin, seqMax = stats seqTimes
    let speedup = seqAvg / parAvg

    printfn "================================================================"
    printfn "RESULTADOS DO BENCHMARK"
    printfn "================================================================"
    printfn "%-12s %10s %10s %10s" "Modo" "Media (s)" "Min (s)" "Max (s)"
    printfn "%-12s %10.3f %10.3f %10.3f" "Sequencial" seqAvg seqMin seqMax
    printfn "%-12s %10.3f %10.3f %10.3f" "Paralelo"   parAvg parMin parMax
    printfn ""
    printfn "Speedup medio      : %.2fx" speedup
    printfn "Cores disponiveis  : %d" Environment.ProcessorCount
    printfn "Eficiencia paralela: %.1f%% (speedup / cores)" (speedup / float Environment.ProcessorCount * 100.0)
    printfn ""
    printfn "Sanity check (Sharpe deve coincidir entre rodadas - determinismo):"
    printfn "  Paralelo  : %.4f" parSharpe
    printfn "  Sequencial: %.4f" seqSharpe
    printfn "  Iguais    : %b" (abs (parSharpe - seqSharpe) < 1e-9)
    printfn "================================================================"

let loadData (args: Map<string,string>) : Tickers * ReturnsMatrix =
    let path = argString args "csv" "data/dow30_returns.csv"
    printfn "Carregando retornos de %s..." path
    let tickers, matrix = loadReturnsFromCsv path
    printfn "  Matriz carregada: %d dias x %d ativos" matrix.Length tickers.Length
    tickers, matrix

[<EntryPoint>]
let main argv =
    let args = parseArgs argv
    let mode = argString args "mode" "sample"

    let config = {
        MaxCombos    = argInt   args "max-combos"  500
        SimsPerCombo = argInt   args "sims"        2000
        K            = argInt   args "k"           25
        MaxWeight    = argFloat args "max-weight"  0.20
        Seed         = argInt   args "seed"        42
        Parallel     = argBool  args "parallel"    true
    }

    let allTickers, allReturns = loadData args

    match mode with
    | "sample" ->
        printBanner (sprintf "PORTFOLIO OPTIMIZATION - Sample (%d combos x %d sims)"
                                config.MaxCombos config.SimsPerCombo)
        let _ = runSimulation "sample" allTickers allReturns config
        0

    | "full" ->
        printBanner (sprintf "PORTFOLIO OPTIMIZATION - Full Run (TODAS combinacoes x %d sims)"
                                config.SimsPerCombo)
        let fullConfig = { config with MaxCombos = -1 }
        let _ = runSimulation "full" allTickers allReturns fullConfig
        0

    | "benchmark" ->
        let runs = argInt args "runs" 5
        runBenchmark allTickers allReturns config runs
        0

    | other ->
        printfn "Modo desconhecido: %s" other
        printfn ""
        printfn "Modos disponiveis:"
        printfn "  --mode=sample           (default) avalia --max-combos combinacoes"
        printfn "  --mode=full             avalia todas as combinacoes C(N,K)"
        printfn "  --mode=benchmark        compara paralelo vs sequencial (N rodadas cada)"
        printfn ""
        printfn "Outras flags:"
        printfn "  --csv=PATH              caminho do CSV de retornos"
        printfn "  --k=25                  ativos por carteira"
        printfn "  --max-combos=500        maximo de combinacoes (sample/benchmark)"
        printfn "  --sims=2000             sorteios de pesos por combinacao"
        printfn "  --max-weight=0.20       cap por ativo"
        printfn "  --seed=42               seed base do RNG"
        printfn "  --parallel=true|false   liga/desliga Async.Parallel (sample/full)"
        printfn "  --runs=5                rodadas por modo (benchmark)"
        1
