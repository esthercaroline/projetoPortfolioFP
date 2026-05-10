# Portfolio Optimization — Projeto 2 (Programação Funcional, F#)

Otimização por **força bruta** da alocação de uma carteira *long-only* do Dow Jones,
buscando maximizar o **Sharpe Ratio**. Escrito em **F# / .NET**, com funções
puras e paralelismo via `Async.Parallel`.

---

## Sumário

- [O problema](#o-problema)
- [Decisão sobre o tamanho da carteira](#decisão-sobre-o-tamanho-da-carteira)
- [Por que F#](#por-que-f-rubrica-b)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Como instalar e rodar](#como-instalar-e-rodar)
- [Parâmetros da CLI](#parâmetros-da-cli)
- [Saída esperada](#saída-esperada)
- [Paralelismo — como foi feito](#paralelismo--como-foi-feito)
- [Benchmark — paralelo vs sequencial](#benchmark--paralelo-vs-sequencial)
- [Funções puras — onde estão](#funções-puras--onde-estão)
- [Itens da rubrica](#itens-da-rubrica)

---

## O problema

Dado o universo das **30 ações** do Dow Jones Industrial Average (composição de 2025):

- Escolher um subconjunto de **K = 25 ativos** &rarr; `C(30, 25) = 142.506` combinações.
- Para cada combinação, sortear vetores de pesos e guardar o de **maior Sharpe**.
- Restrições: `wᵢ ≥ 0`,  `wᵢ ≤ 0,20`,  `Σ wᵢ = 1`.
- Objetivo: maximizar `SR = μ / σ`.

Fórmulas (implementadas como funções puras em `Portfolio.fs`):

```
Retorno anualizado  :  μ = (mediasDiárias · w) × 252
Volatilidade anual  :  σ = √(wᵀ C w) × √252
Sharpe Ratio        :  SR = μ / σ
```

A taxa livre de risco `r_free` é desconsiderada (como sugere o enunciado), já que
todas as carteiras a usariam de forma idêntica e ela some na comparação relativa.

---

## Decisão sobre o tamanho da carteira

O enunciado original pedia **K = 20**, gerando `C(30, 20) = 30.045.015` combinações
&times; 1M sorteios = **30 trilhões de simulações**. Conforme o professor permitiu
em um comunicado posterior:


Adotei **K = 25**, que dá:

| K  | Combinações C(30, K) |
|----|---------------------:|
| 20 | 30.045.015           |
| 25 | **142.506**          |
| 26 | 593.775              |

K = 25 foi escolhido por ser o **maior espaço de busca** ainda computacionalmente
factível dentro da nova regra. O parâmetro `--k` permite mudar K na CLI.

---

## Por que F# (rubrica B+)

F# é uma linguagem **funcional** nativa do .NET — o projeto se enquadra na
faixa **B+** da rubrica. Pilares funcionais presentes:

- **Funções puras** em `Portfolio.fs` — `combinations`, `sliceColumns`,
  `meanReturns`, `covarianceMatrix`, `dot`, `quadraticForm`,
  `annualizedReturn`, `annualizedVolatility`, `sharpeRatio`, `sampleWeights`,
  `evaluateFast`, `evaluate`. Todas obedecem a propriedade *mesma entrada → mesma saída*.
- **Pipeline funcional** em `Simulate.findBestPortfolio`:

  ```
  combos
  |> Array.map  bestPortfolioForCombinationAsync
  |> Async.Parallel
  |> Async.RunSynchronously
  |> Array.filter   (fun p -> p.Sharpe é finito)
  |> Array.maxBy    (fun p -> p.Sharpe)
  ```

- **Paralelismo via `Async.Parallel`**: cada combinação é uma tarefa pura, sem
  estado compartilhado — trivialmente seguro para paralelizar.
- **Imutabilidade como default**; a mutação de `mutable` está **escondida dentro
  de funções puras** (loops de acumulação numérica), sem escapar de escopo.
- **Determinismo**: a seed do RNG de cada combinação é derivada de um *hash*
  determinístico dos índices da combinação + seed base — assim, mesmos
  parâmetros geram exatamente o mesmo resultado, **independentemente da ordem
  de execução paralela**. O benchmark abaixo confirma isso empiricamente:
  Sharpe idêntico em todas as 10 rodadas (5 paralelas + 5 sequenciais).

---

## Estrutura do projeto

```
projetoPortfolioFP/
├── README.md
├── projetoPortfolioFP.fsproj
├── data/
│   └── dow30_returns.csv          # 126 dias × 30 ativos (2º sem. 2025)
├── Types.fs                        # tipos do domínio (imutáveis)
├── DataLoader.fs                   # leitura do CSV de retornos
├── Portfolio.fs                    # FUNÇÕES PURAS — toda a matemática
├── Simulate.fs                     # pipeline paralelo (Async.Parallel)
└── Program.fs                      # CLI (sample / full / benchmark)
```

---

## Como instalar e rodar

### Pré-requisito

- **.NET 8 ou superior** — instale em https://dotnet.microsoft.com/download

Verifique:

```bash
dotnet --version
```

> Observação: o `.fsproj` está configurado para `net10.0` (versão usada no
> desenvolvimento). Se você tiver apenas .NET 8 instalado, edite o
> `<TargetFramework>` no arquivo `.fsproj` para `net8.0`.

### Build

```bash
dotnet build -c Release
```

### Sample rápido (default)

Avalia 500 combinações × 2.000 sorteios = 1M de simulações.
Termina em ~1 segundo:

```bash
dotnet run -c Release
```

### Run completo

Avalia **todas as 142.506 combinações × `--sims` sorteios cada**:

```bash
dotnet run -c Release -- --mode=full --sims=2000
```

### Benchmark — paralelo vs sequencial (extensão da rubrica, +½ conceito)

5 rodadas em cada modo, com tabela de tempos e *speedup*:

```bash
dotnet run -c Release -- --mode=benchmark --max-combos=2000 --sims=2000 --runs=5
```

---

## Parâmetros da CLI

| Flag | Default | Significado |
|------|---------|-------------|
| `--mode` | `sample` | `sample`, `full`, ou `benchmark` |
| `--csv` | `data/dow30_returns.csv` | caminho do CSV de retornos |
| `--k` | `25` | tamanho de cada carteira |
| `--max-combos` | `500` | quantas combinações avaliar (sample/benchmark) |
| `--sims` | `2000` | sorteios de pesos por combinação |
| `--max-weight` | `0.20` | restrição de concentração `wᵢ ≤ 0,20` |
| `--seed` | `42` | seed base do RNG (reprodutibilidade) |
| `--parallel` | `true` | liga/desliga `Async.Parallel` (sample/full) |
| `--runs` | `5` | rodadas por modo (benchmark) |

---

## Saída esperada

```
+======================================================================+
| PORTFOLIO OPTIMIZATION - Sample (500 combos x 2000 sims)             |
+======================================================================+

Carregando retornos de data/dow30_returns.csv...
  Matriz carregada: 126 dias x 30 ativos

Configuracao [sample]:
  Modo paralelo      : true
  K (ativos/carteira): 25
  Max combinacoes    : 500  (de C(30,25) = 142506)
  Sims/combinacao    : 2,000
  Seed base          : 42
  Cap por ativo      : 0.20
  Cores disponiveis  : 10
  Total de simulacoes: 1,000,000

Rodando...
Tempo total: 0.52 s

  ================================================================
  MELHOR CARTEIRA ENCONTRADA
  ================================================================
  Retorno anualizado    :    39.11%
  Volatilidade anualiz. :     9.36%
  Sharpe Ratio          :    4.1800

  Ativos (25) e pesos:
    JNJ     19.85%  ###################
    NVDA    11.43%  ###########
    CAT      9.94%  #########
    AAPL     7.43%  #######
    ...
  Soma dos pesos        :  1.0000
  ================================================================
```

---

## Paralelismo — como foi feito

Cada combinação de ativos é avaliada por uma **função pura** — sem I/O, sem
mutação compartilhada. O `Async.Parallel` distribui as tarefas entre todos os
cores. Trecho central de `Simulate.fs`:

```fsharp
combos
|> Array.map (bestPortfolioForCombinationAsync allReturns config)
|> Async.Parallel
|> Async.RunSynchronously
|> Array.filter (fun p -> p.Sharpe > Double.NegativeInfinity && not (Double.IsNaN p.Sharpe))
|> Array.maxBy  (fun p -> p.Sharpe)
```

A seed de cada combinação é derivada deterministicamente:

```fsharp
let derivedSeed (baseSeed: int) (combo: int[]) : int =
    let mutable h = baseSeed
    for i in 0 .. combo.Length - 1 do
        h <- h * 31 + combo.[i] + 17
    h
```

Garantia: mesmos `--seed`, `--k`, `--sims` e dados &rArr; mesmo resultado, sempre.

### Por que paralelizar entre combinações (e não entre sorteios de pesos)

São 142.506 combinações independentes — granulosidade ideal para o `ThreadPool`
do .NET, que distribui por work-stealing entre os cores. Paralelizar
por sorteio de pesos teria *overhead* relativamente maior em relação ao trabalho
real (cada sorteio é micro-segundos de cálculo).

---

## Benchmark — paralelo vs sequencial

Comparação executando 5 rodadas em cada modo, sobre 2.000 combinações × 2.000
sorteios = **4 milhões de simulações por rodada**. Máquina: **Apple Silicon, 10
cores**, .NET 10, build Release.

| Modo        | Média (s) | Mín (s) | Máx (s) |
|-------------|----------:|--------:|--------:|
| Sequencial  |     4.138 |   4.120 |   4.154 |
| Paralelo    |     0.766 |   0.668 |   1.059 |

- **Speedup médio: 5.40×**
- **Eficiência paralela: 54%** (speedup ÷ 10 cores)
- **Sharpe idêntico em todas as 10 rodadas: 4.2997** &rarr; determinismo
  empiricamente verificado, independente da ordem de execução paralela.

A perda de eficiência em relação ao linear (10×) é esperada e tem causas conhecidas:
*overhead* de scheduling do `Async.Parallel`, contenção de memória entre threads
(cada uma fatia a matriz de retornos), e variação do tempo por combinação
(rejeições do sampler de Dirichlet variam). Reproduzível com:

```bash
dotnet run -c Release -- --mode=benchmark --max-combos=2000 --sims=2000 --runs=5
```

---

## Funções puras — onde estão

Toda a matemática vive em `Portfolio.fs` como funções puras, sem
side-effects externos (mutações são locais a loops de acumulação):

| Função | Tipo | O que faz |
|--------|------|-----------|
| `combinations n k` | `int -> int -> int[][]` | gera todas as `C(n,k)` combinações |
| `sliceColumns` | `ReturnsMatrix -> int[] -> ReturnsMatrix` | extrai as colunas escolhidas |
| `meanReturns` | `ReturnsMatrix -> float[]` | média por coluna |
| `covarianceMatrix` | `ReturnsMatrix -> float[] -> float[][]` | covariância amostral |
| `dot` | `float[] -> float[] -> float` | produto interno |
| `quadraticForm` | `float[][] -> float[] -> float` | `wᵀ C w`, usando simetria |
| `annualizedReturn` | `float[] -> float[] -> float` | `μ × 252` |
| `annualizedVolatility` | `float[][] -> float[] -> float` | `σ × √252` |
| `sharpeRatio` | `float -> float -> float` | `μ / σ` |
| `sampleWeights` | `Random -> int -> float -> float[]` | Dirichlet(1,…,1) com rejeição se `wᵢ > cap` |
| `evaluateFast` | `float[] -> float[][] -> float[] -> (float, float, float)` | avaliação no laço quente |
| `evaluate` | `ReturnsMatrix -> int[] -> float[] -> EvaluatedPortfolio` | avaliação completa (uso ad-hoc) |

A geração de pesos usa amostragem de **Dirichlet(1,…,1)** via Exp(1) normalizada
(método clássico), com **rejeição** se algum peso violar o cap de 20%. Como
`K=25` e o cap é `0,20`, e a média natural é `1/25 = 0,04`, a taxa de rejeição é
baixíssima.

---

## Itens da rubrica

| Item | Status | Onde |
|------|--------|------|
| Linguagem funcional (F#) — faixa **B+** | ✅ | todo o projeto |
| Combinatória C(30, K) com K = 25 (relaxação do prof.) | ✅ | `Portfolio.combinations`, default `--k=25` |
| Sorteio de pesos com restrições (`wᵢ ≥ 0`, `wᵢ ≤ 0,20`, soma = 1) | ✅ | `Portfolio.sampleWeights` |
| Pipeline com elementos funcionais (map / filter / reduce) | ✅ | `Simulate.findBestPortfolio` |
| Funções puras nas partes paralelizadas | ✅ | `Portfolio.fs` inteiro + `bestPortfolioForCombination` |
| Paralelismo (`Async.Parallel`) | ✅ | `Simulate.findBestPortfolio` |
| **Bônus: comparação paralelo vs sequencial, 5+ rodadas** (+½ conceito) | ✅ | `--mode=benchmark` &rarr; speedup de **5.40×** demonstrado |
| README descritivo, com instalação e execução | ✅ | este arquivo |