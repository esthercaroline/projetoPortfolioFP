module PortfolioOptimization.Portfolio

open System
open PortfolioOptimization.Types

/// Número de pregões num ano — usado para anualizar.
[<Literal>]
let TradingDays = 252.0

// ===========================================================================
// COMBINATÓRIA — gera todas as combinações C(n, k) como vetores de índices.
// Implementação iterativa em ordem lexicográfica. Função pura.
// ===========================================================================

/// PURA: gera todas as combinações C(n, k) como int[]'s ordenados.
/// Para C(30, 25) = 142.506 itens, cabe folgado em memória.
let combinations (n: int) (k: int) : int[][] =
    if k < 0 || k > n then [||]
    elif k = 0 then [| [||] |]
    else
        let result = ResizeArray<int[]>()
        let combo = Array.init k id   // [0; 1; 2; ...; k-1]

        let mutable running = true
        while running do
            result.Add(Array.copy combo)
            // Avança para a próxima combinação em ordem lexicográfica
            let mutable i = k - 1
            while i >= 0 && combo.[i] = n - k + i do
                i <- i - 1
            if i < 0 then
                running <- false
            else
                combo.[i] <- combo.[i] + 1
                for j in i + 1 .. k - 1 do
                    combo.[j] <- combo.[j - 1] + 1
        result.ToArray()

// ===========================================================================
// SLICING — extrai as colunas (ativos) escolhidos da matriz total de retornos.
// PURO: retorna uma nova matriz, não muta a entrada.
// ===========================================================================

let sliceColumns (returns: ReturnsMatrix) (assetIndices: int[]) : ReturnsMatrix =
    returns
    |> Array.map (fun row ->
        Array.init assetIndices.Length (fun j -> row.[assetIndices.[j]]))

// ===========================================================================
// ESTATÍSTICAS — média e covariância da matriz já fatiada.
// PUROS. Calculadas UMA vez por combinação de ativos (não por sorteio de pesos).
// ===========================================================================

/// PURO: vetor com a média de cada coluna (ativo).
let meanReturns (returns: ReturnsMatrix) : float[] =
    let nDays = returns.Length
    if nDays = 0 then [||]
    else
        let nAssets = returns.[0].Length
        let acc = Array.zeroCreate<float> nAssets
        for d in 0 .. nDays - 1 do
            let row = returns.[d]
            for a in 0 .. nAssets - 1 do
                acc.[a] <- acc.[a] + row.[a]
        let denom = float nDays
        Array.map (fun s -> s / denom) acc

/// PURO: matriz de covariância amostral (divisor n-1).
let covarianceMatrix (returns: ReturnsMatrix) (means: float[]) : float[][] =
    let nDays = returns.Length
    let nAssets = means.Length
    let cov = Array.init nAssets (fun _ -> Array.zeroCreate<float> nAssets)
    if nDays < 2 then cov
    else
        let denom = float (nDays - 1)
        for d in 0 .. nDays - 1 do
            let row = returns.[d]
            for i in 0 .. nAssets - 1 do
                let di = row.[i] - means.[i]
                let covI = cov.[i]
                for j in i .. nAssets - 1 do
                    covI.[j] <- covI.[j] + di * (row.[j] - means.[j])
        // Normaliza e espelha — apenas a metade superior foi acumulada
        for i in 0 .. nAssets - 1 do
            for j in i .. nAssets - 1 do
                let v = cov.[i].[j] / denom
                cov.[i].[j] <- v
                cov.[j].[i] <- v
        cov

// ===========================================================================
// MÉTRICAS DE CARTEIRA — todas puras, sobre means/cov já calculados.
//   μ_anual = (means · w) × 252
//   σ_anual = √(wᵀ C w) × √252
//   SR     = μ_anual / σ_anual
// ===========================================================================

/// PURO: produto interno means · w.
let inline dot (means: float[]) (w: Weights) : float =
    let mutable s = 0.0
    for i in 0 .. w.Length - 1 do
        s <- s + means.[i] * w.[i]
    s

/// PURO: forma quadrática wᵀ C w. Usa simetria de C:
///   wᵀCw = Σᵢ wᵢ² Cᵢᵢ + 2·Σ_{i<j} wᵢ Cᵢⱼ wⱼ
let inline quadraticForm (cov: float[][]) (w: Weights) : float =
    let n = w.Length
    let mutable s = 0.0
    for i in 0 .. n - 1 do
        let covI = cov.[i]
        let wi = w.[i]
        s <- s + wi * wi * covI.[i]
        for j in i + 1 .. n - 1 do
            s <- s + 2.0 * wi * covI.[j] * w.[j]
    s

let inline annualizedReturn (means: float[]) (w: Weights) : float =
    (dot means w) * TradingDays

let inline annualizedVolatility (cov: float[][]) (w: Weights) : float =
    sqrt (quadraticForm cov w) * sqrt TradingDays

let inline sharpeRatio (mu: float) (sigma: float) : float =
    if sigma <= 0.0 then 0.0 else mu / sigma

// ===========================================================================
// AMOSTRAGEM DE PESOS — Dirichlet(1,...,1) com rejeição se algum wᵢ > maxWeight.
// PURO em relação ao gerador passado (Random é estado local, não compartilhado).
// ===========================================================================

let sampleWeights (rng: Random) (k: int) (maxWeight: float) : Weights =
    // Amostra Exp(1) = -ln(U), normaliza → Dirichlet(1,...,1) uniforme no simplex.
    // Rejeita se algum wᵢ > cap. Para K=25 e cap=0.20, cap >> mean=0.04 → aceitação alta.
    let w = Array.zeroCreate<float> k
    let mutable accept = false
    while not accept do
        let mutable sum = 0.0
        for i in 0 .. k - 1 do
            // Evita log(0) caso NextDouble() retorne 0 exato
            let u = max 1e-300 (rng.NextDouble())
            let x = -log u
            w.[i] <- x
            sum <- sum + x
        let invSum = 1.0 / sum
        let mutable maxW = 0.0
        for i in 0 .. k - 1 do
            w.[i] <- w.[i] * invSum
            if w.[i] > maxW then maxW <- w.[i]
        if maxW <= maxWeight then accept <- true
    w

// ===========================================================================
// AVALIAÇÃO RÁPIDA — usa means/cov já pré-computados para a combinação.
// É esta função que roda 1M de vezes por combinação no laço quente.
// ===========================================================================

/// PURO: avalia uma carteira dado means+cov pré-computados. Retorna (μ, σ, SR).
let evaluateFast
    (means: float[])
    (cov: float[][])
    (weights: Weights)
    : float * float * float =
    let mu = annualizedReturn means weights
    let sigma = annualizedVolatility cov weights
    let sr = sharpeRatio mu sigma
    mu, sigma, sr

// ===========================================================================
// AVALIAÇÃO COMPLETA — para uso ad-hoc (out-of-sample, debugging, validação).
// Recalcula means/cov; cara, NÃO use no laço quente.
// ===========================================================================

let evaluate
    (returns: ReturnsMatrix)
    (assetIndices: int[])
    (weights: Weights)
    : EvaluatedPortfolio =
    let sliced = sliceColumns returns assetIndices
    let means = meanReturns sliced
    let cov = covarianceMatrix sliced means
    let mu, sigma, sr = evaluateFast means cov weights
    {
        AssetIndices = assetIndices
        Weights = weights
        AnnualReturn = mu
        AnnualVolatility = sigma
        Sharpe = sr
    }
