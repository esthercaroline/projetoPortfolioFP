module PortfolioOptimization.Simulate

open System
open PortfolioOptimization.Types
open PortfolioOptimization.Portfolio

// ===========================================================================
// SEED DETERMINÍSTICA POR COMBINAÇÃO
//   Mesmos parâmetros de entrada → mesmo resultado, independentemente da
//   ordem de execução paralela. Garante reprodutibilidade.
// ===========================================================================

/// PURO: deriva seed única a partir da seed base + índices da combinação.
let derivedSeed (baseSeed: int) (combo: int[]) : int =
    let mutable h = baseSeed
    for i in 0 .. combo.Length - 1 do
        // Mistura simples (estilo string-hash) — boa o suficiente para descorrelacionar
        h <- h * 31 + combo.[i] + 17
    h

// ===========================================================================
// MELHOR CARTEIRA POR COMBINAÇÃO
//   Função PURA dado allReturns e config (seed deriva de combo + baseSeed).
//   Pré-computa means/cov UMA vez, depois sorteia simsPerCombo pesos e
//   guarda apenas o melhor Sharpe encontrado.
// ===========================================================================

/// PURA (dado os argumentos): retorna a melhor carteira para uma combinação.
let bestPortfolioForCombination
    (allReturns: ReturnsMatrix)
    (config: SimConfig)
    (combo: int[])
    : EvaluatedPortfolio =
    // Pré-computa stats da combinação — uma vez por combo
    let sliced = sliceColumns allReturns combo
    let means  = meanReturns sliced
    let cov    = covarianceMatrix sliced means

    // RNG local à thread — Random NÃO é thread-safe, mas aqui cada combo
    // tem seu próprio Random isolado, sem compartilhamento.
    let rng = Random(derivedSeed config.Seed combo)
    let k = combo.Length

    // Inicializa com o primeiro sorteio para evitar pesos zerados em caso
    // de NaN ou simulações degeneradas.
    let firstW = sampleWeights rng k config.MaxWeight
    let firstMu, firstSigma, firstSr = evaluateFast means cov firstW
    let mutable bestSharpe  = firstSr
    let mutable bestMu      = firstMu
    let mutable bestSigma   = firstSigma
    let mutable bestWeights = firstW

    for _ in 2 .. config.SimsPerCombo do
        let w = sampleWeights rng k config.MaxWeight
        let mu, sigma, sr = evaluateFast means cov w
        if sr > bestSharpe then
            bestSharpe  <- sr
            bestMu      <- mu
            bestSigma   <- sigma
            bestWeights <- w   // sampleWeights aloca novo array a cada chamada

    {
        AssetIndices     = combo
        Weights          = bestWeights
        AnnualReturn     = bestMu
        AnnualVolatility = bestSigma
        Sharpe           = bestSharpe
    }

// ===========================================================================
// PIPELINE: combinações → melhor de cada → melhor global
// ===========================================================================

/// Versão Async para usar com Async.Parallel.
let bestPortfolioForCombinationAsync
    (allReturns: ReturnsMatrix)
    (config: SimConfig)
    (combo: int[])
    : Async<EvaluatedPortfolio> =
    async {
        return bestPortfolioForCombination allReturns config combo
    }

/// Pipeline funcional: gera combinações, avalia cada uma (paralelo ou sequencial),
/// reduz para a melhor global pelo Sharpe.
let findBestPortfolio
    (allReturns: ReturnsMatrix)
    (nAssetsTotal: int)
    (config: SimConfig)
    : EvaluatedPortfolio =

    // Gera todas as combinações C(nAssetsTotal, K)
    let allCombos = combinations nAssetsTotal config.K

    // Aplica o cap de --max-combos (útil em modo "sample" para testes rápidos)
    let combos =
        if config.MaxCombos < 0 || config.MaxCombos >= allCombos.Length then
            allCombos
        else
            Array.sub allCombos 0 config.MaxCombos

    // Pipeline funcional principal
    if config.Parallel then
        combos
        |> Array.map (bestPortfolioForCombinationAsync allReturns config)
        |> Async.Parallel
        |> Async.RunSynchronously
        |> Array.filter (fun p -> p.Sharpe > Double.NegativeInfinity && not (Double.IsNaN p.Sharpe))
        |> Array.maxBy (fun p -> p.Sharpe)
    else
        combos
        |> Array.map (bestPortfolioForCombination allReturns config)
        |> Array.filter (fun p -> p.Sharpe > Double.NegativeInfinity && not (Double.IsNaN p.Sharpe))
        |> Array.maxBy (fun p -> p.Sharpe)
