using System.Text.RegularExpressions;

namespace Operacoes.Architecture.Tests;

public sealed partial class HubUnavailableConventionTests
{
    private static readonly string RepoRoot = LocalizarRaizDoRepo();

    [Fact]
    public void SomenteCatalogoErrors_DeclaraErrorTypeUnavailable()
    {
        var arquivos = ListarArquivosCs(Path.Combine(RepoRoot, "src"));

        Assert.True(
            arquivos.Count > 0,
            $"esta asserção só é uma convenção real se houver arquivos .cs em src/ para inspecionar; " +
            $"encontrados: {arquivos.Count} em '{RepoRoot}'.");

        var ocorrencias = EncontrarOcorrencias(arquivos, PadraoErrorTypeUnavailable());

        Assert.True(
            ocorrencias.Count > 0,
            "esperava encontrar ErrorType.Unavailable declarado em algum lugar (CatalogoErrors.cs); " +
            "se este teste falhar aqui, o scanner parou de enxergar o token, e a checagem de exclusividade " +
            "abaixo virou vacuidade.");

        var foraDoEsperado = ocorrencias
            .Where(o => !o.Contains("CatalogoErrors.cs")
                && !o.Contains("ResultExtensions.cs")
                && !o.Contains("HubCatalogoClient.cs"))
            .ToList();

        Assert.True(
            foraDoEsperado.Count == 0,
            "ErrorType.Unavailable só deveria ser declarado em Operacoes.Application/Catalogo/CatalogoErrors.cs " +
            "(F3, docs/ROADMAP.md), fora do mapeamento central de ResultExtensions.cs — outro arquivo " +
            "declarando seu próprio Error com este ErrorType permitiria 'não houve veredito' nascer fora do " +
            "HubCatalogoClient. Ocorrências fora do esperado:\n" + string.Join('\n', foraDoEsperado));
    }

    [Fact]
    public void SomenteHubCatalogoClient_DevolveCatalogoErrorsHubIndisponivel()
    {
        var arquivos = ListarArquivosCs(Path.Combine(RepoRoot, "src"));

        Assert.True(
            arquivos.Count > 0,
            $"esta asserção só é uma convenção real se houver arquivos .cs em src/ para inspecionar; " +
            $"encontrados: {arquivos.Count} em '{RepoRoot}'.");

        var ocorrencias = EncontrarOcorrencias(arquivos, PadraoHubIndisponivel());

        Assert.True(
            ocorrencias.Count > 0,
            "esperava encontrar CatalogoErrors.HubIndisponivel usado em algum lugar (HubCatalogoClient.cs); " +
            "se este teste falhar aqui, o scanner parou de enxergar o token, e a checagem de exclusividade " +
            "abaixo virou vacuidade.");

        var foraDoClient = ocorrencias.Where(o => !o.Contains("HubCatalogoClient.cs")).ToList();

        Assert.True(
            foraDoClient.Count == 0,
            "CatalogoErrors.HubIndisponivel só deveria ser devolvido em " +
            "Operacoes.Infrastructure/Catalogo/HubCatalogoClient.cs (F3, docs/ROADMAP.md, 'A regra de " +
            "camada') — qualquer outro produtor rompe a garantia de que 503 só nasce de uma falha real ao " +
            "falar com o Hub. Ocorrências fora do esperado:\n" + string.Join('\n', foraDoClient));
    }

    private static List<string> EncontrarOcorrencias(IReadOnlyList<string> arquivos, Regex padrao)
    {
        var ocorrencias = new List<string>();

        foreach (var arquivo in arquivos)
        {
            var linhas = File.ReadAllLines(arquivo);
            for (var i = 0; i < linhas.Length; i++)
            {
                if (padrao.IsMatch(linhas[i]))
                {
                    ocorrencias.Add($"{arquivo}:{i + 1}: {linhas[i].Trim()}");
                }
            }
        }

        return ocorrencias;
    }

    private static List<string> ListarArquivosCs(string diretorio)
    {
        if (!Directory.Exists(diretorio))
        {
            return [];
        }

        return Directory.EnumerateFiles(diretorio, "*.cs", SearchOption.AllDirectories)
            .Where(caminho => !ContemSegmentoBinOuObj(caminho))
            .ToList();
    }

    private static bool ContemSegmentoBinOuObj(string caminho)
    {
        var segmentos = caminho.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segmentos.Any(s => s is "bin" or "obj");
    }

    private static string LocalizarRaizDoRepo()
    {
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);

        while (diretorio is not null && !File.Exists(Path.Combine(diretorio.FullName, "Operacoes.sln")))
        {
            diretorio = diretorio.Parent;
        }

        if (diretorio is null)
        {
            throw new InvalidOperationException(
                $"Não foi possível localizar a raiz do repo (Operacoes.sln) subindo a partir de " +
                $"'{AppContext.BaseDirectory}'.");
        }

        return diretorio.FullName;
    }

    [GeneratedRegex(@"ErrorType\.Unavailable")]
    private static partial Regex PadraoErrorTypeUnavailable();

    [GeneratedRegex(@"CatalogoErrors\.HubIndisponivel")]
    private static partial Regex PadraoHubIndisponivel();
}
