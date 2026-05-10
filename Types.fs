module PortfolioOptimization.Types

/// Vetor de pesos da carteira. Soma = 1, cada wᵢ ∈ [0, 0.20].
type Weights = float[]

/// Matriz de retornos diária. Linhas = dias, colunas = ativos.
/// Acessada como Returns.[dia].[ativo].
type ReturnsMatrix = float[][]

/// Lista de tickers (na mesma ordem das colunas da matriz de retornos).
type Tickers = string[]

/// Configuração de uma simulação.
type SimConfig = {
    /// Quantas combinações C(30, K) avaliar (cap, para "sample"); -1 = todas.
    MaxCombos: int
    /// Quantos sorteios de pesos por combinação.
    SimsPerCombo: int
    /// Tamanho de cada subconjunto de ativos (K em C(N,K)).
    K: int
    /// Cap por ativo (ex: 0.20).
    MaxWeight: float
    /// Seed base — cada combinação deriva sua própria seed determinística desta.
    Seed: int
    /// Liga/desliga Async.Parallel.
    Parallel: bool
}

/// Resultado da avaliação de uma carteira específica.
type EvaluatedPortfolio = {
    /// Índices (na matriz original de N ativos) das K ações selecionadas.
    AssetIndices: int[]
    /// Pesos correspondentes — mesma ordem de AssetIndices, soma = 1.
    Weights: Weights
    /// Retorno anualizado (μ × 252).
    AnnualReturn: float
    /// Volatilidade anualizada (σ × √252).
    AnnualVolatility: float
    /// Sharpe Ratio = AnnualReturn / AnnualVolatility (rfree desconsiderado).
    Sharpe: float
}
