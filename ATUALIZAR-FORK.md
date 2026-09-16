# Como atualizar este fork

Este fork (`Erchies/xdm`) junta três projetos:

| O que | Repositório de base | Onde está aqui |
|---|---|---|
| O XDM em si | [subhra74/xdm](https://github.com/subhra74/xdm) | tudo em `app/` |
| Listagem do GoFile | [ltsdw/gofile-downloader](https://github.com/ltsdw/gofile-downloader), via o projeto local `Documents\gofile-downloader-gui` | `app/XDM/XDM.Core/FileHosts/GofileResolver.cs` |
| Listagem do Bunkr | [ZeroHackz/BunkrDownloader-Portable](https://github.com/ZeroHackz/BunkrDownloader-Portable), que segue [Lysagxra/BunkrDownloader](https://github.com/Lysagxra/BunkrDownloader) | `app/XDM/XDM.Core/FileHosts/BunkrResolver.cs` e `BunkrHtml.cs` |

O XDM se atualiza com `git merge`. GoFile e Bunkr são em Python e foram **reescritos em C#**, então não dá para dar merge neles: você vê o que mudou lá e aplica a mesma mudança no arquivo C# correspondente. As tabelas abaixo dizem onde.

Os comandos usam `git`, `gh` (GitHub CLI) e `dotnet` (SDK 8). Rode no **Git Bash**, a partir da pasta do fork.

---

## 1. Linha de base

Versões de cada base usadas como referência. **Atualize esta tabela sempre que portar algo** (passo 6).

| Base | Branch | Commit de referência | Data do commit |
|---|---|---|---|
| subhra74/xdm | `master` | `1ca5a25aae007826c859c81bea494e7c102e1242` | 2023-06-19 |
| ZeroHackz/BunkrDownloader-Portable | `release/latest` | `b3ee61135755fd34ffeebd9f95658ec7a648e2d7` | 2026-08-03 |
| Lysagxra/BunkrDownloader | `main` | `331061addfbc16765dc2e91056364ea6ca65f238` | 2026-08-24 |
| ltsdw/gofile-downloader | `main` | `640b8a41560808208cfdcf74b135107b8956b625` | 2026-08-14 |
| gofile-downloader-gui (local) | — | sem git | — |

Referências conferidas em 2026-09-16.

---

## 2. Ver se alguma base mudou

Um comando mostra, para cada base, os commits novos desde a linha de base e os arquivos alterados:

```bash
while read repo base branch; do
  echo "== $repo ($branch)"
  gh api "repos/$repo/compare/$base...$branch" --jq '"commits novos: \(.ahead_by)", (.commits[] | "  " + .sha[0:7] + " " + (.commit.message | split("\n")[0])), (.files[] | "  arquivo: " + .filename)'
done <<'EOF'
subhra74/xdm 1ca5a25aae007826c859c81bea494e7c102e1242 master
ZeroHackz/BunkrDownloader-Portable b3ee61135755fd34ffeebd9f95658ec7a648e2d7 release/latest
Lysagxra/BunkrDownloader 331061addfbc16765dc2e91056364ea6ca65f238 main
ltsdw/gofile-downloader 640b8a41560808208cfdcf74b135107b8956b625 main
EOF
```

Se tudo mostrar `commits novos: 0`, não há nada a fazer. Para ver o diff completo de um arquivo específico:

```bash
gh api repos/ltsdw/gofile-downloader/compare/640b8a41560808208cfdcf74b135107b8956b625...main --jq '.files[] | select(.filename=="gofile-downloader.py") | .patch'
```

Troque o repositório, o commit, o branch e o nome do arquivo conforme a base.

---

## 3. Atualizar o XDM (merge normal)

O remoto `upstream` aponta para `subhra74/xdm` e `origin` para o seu fork. Se um clone novo não tiver o `upstream`:

```bash
git remote add upstream https://github.com/subhra74/xdm.git
```

Traga as mudanças para o branch onde está a integração (`master`, se o `feature/gofile-bunkr` já foi mesclado nele):

```bash
git fetch upstream
git checkout master
git merge upstream/master
```

Não use `gh repo sync --force` nem o botão "Discard commits" do GitHub: eles apagam a integração.

### Onde podem aparecer conflitos

Os arquivos novos (`FileHosts/`, `FileHostDownloadUIController.cs`, `IFileHostDownloadView.cs`, `Dialogs/FileHostDownload/`, `XDM.FileHosts.Tests/`) não existem no original e não entram em conflito. Estes arquivos do XDM original foram editados e podem conflitar:

| Arquivo | O que a integração acrescentou |
|---|---|
| `app/XDM/XDM.Core/Config.cs` | propriedades `FileHostCreateSubfolders`, `GofileAccountToken`, `GofileWebsiteTokenSalt`, `BunkrIncludeFilter`, `BunkrIgnoreFilter`, `BunkrCleanName` |
| `app/XDM/XDM.Core/IO/ConfigIO.cs` | leitura e gravação dessas propriedades (os dois lados precisam continuar batendo) |
| `app/XDM/XDM.Core/ApplicationCore.cs` | `using XDM.Core.FileHosts`; em `DownloadFinished`, a chamada `FileHostFileDates.Apply` (data de envio do Bunkr); em `GetStateFiles`, o arquivo `<id>.filedate` |
| `app/XDM/XDM.Core/IPlatformUIService.cs` | método `ShowFileHostDownloadWindow` |
| `app/XDM/XDM.Core/UI/NewDownloadDialogUIController.cs` | desvio de links GoFile/Bunkr para a janela nova |
| `app/XDM/XDM.Core/XDM.Core.projitems` | lista dos arquivos novos |
| `app/XDM/XDM.Wpf.UI/MainWindow/MainWindow.xaml` e `.xaml.cs` | item de menu `menuFileHostDownload` (índice 3 do menu Novo) |
| `app/XDM/XDM.Wpf.UI/MainWindow/WpfPlatformUIService.cs` | abre a janela nova |
| `app/XDM/XDM.Gtk.UI/GtkPlatformUIService.cs` | aviso de "não disponível" no Linux |
| `app/XDM/XDM.Wpf.UI/XDM.Wpf.UI.csproj` | compila e copia o `xdm-guide.exe` (integração com navegadores) |
| `app/XDM/Lang/English.txt` e `Portuguese Brazil.txt` | chaves `MENU_FILEHOST_DOWNLOAD` e `FH_*` no fim do arquivo |

Ao resolver um conflito, mantenha os dois lados: o que veio do original e o que a integração acrescentou. Se o original **adicionar um método** a `IPlatformUIService`, a compilação acusa em `WpfPlatformUIService` e `GtkPlatformUIService` até você implementar nos dois.

Depois do merge, siga o passo 6.

---

## 4. Portar uma mudança do GoFile

A base é o `gofile-downloader.py` do ltsdw, mais as correções do seu `gofile-downloader-gui` (`src/gofile_core.py`). Tudo fica em `GofileResolver.cs`.

### Correção rápida sem recompilar

A mudança mais comum do GoFile é trocar o **salt** do `X-Website-Token`. Os sintomas no log da janela são "não foi possível criar uma conta de visitante" ou "o GoFile recusou a listagem". No script do ltsdw, o salt é a última parte da string em `generate_website_token` (procure por `::en-US::`). Cole o valor novo em **Novo → Download do GoFile / Bunkr → Opções → Salt do X-Website-Token**. O valor fica salvo nas configurações. Depois vale atualizar o padrão no código também.

### Onde cada coisa está

| No Python | No C# (`GofileResolver.cs`) |
|---|---|
| salt (`12af056dacea0b`) | `DefaultWebsiteTokenSalt` |
| janela de 4 h (`14400`) | `TokenSlotSeconds` |
| `generate_website_token` (`UA::en-US::token::slot::salt`, sha256) | `GenerateWebsiteToken` |
| `https://api.gofile.io` | `ApiBase` |
| `POST /accounts` (conta de visitante) | `Authenticate` |
| `GET /contents/{id}?cache=true&sortField=createTime&sortDirection=1&password=sha256` | `FetchContent` |
| status `error-passwordRequired`, `error-passwordWrong`, `error-notFound` | `switch` em `FetchContent` |
| headers da API (`X-Website-Token`, `X-BL`, `Authorization`, `Cookie`) | `ApiHeaders` |
| headers do download do arquivo | `DownloadHeaders` (mais `Cookies = "accountToken=..."` em `AddFile`) |
| percorrer pastas e `children` | `Walk` e `AddFile` |
| user agent padrão (`Mozilla/5.0`) | `DefaultUserAgent` |
| links `gofile.io/d/...` e `gofile.io/w/...` | `LinkRegex` |
| `sanitize_name`, nomes repetidos | `FileHostNames.Sanitize`, `FileHostNameRegistry` (`FileHostNames.cs`) |

### Testes que dependem desses valores

`app/XDM/XDM.FileHosts.Tests/GofileResolverTests.cs` compara o token e o hash da senha com valores calculados em Python. Se a fórmula ou o salt mudar, recalcule e troque os valores esperados:

```bash
py -c "from hashlib import sha256; print(sha256(b'Mozilla/5.0::en-US::abc::123456::NOVO_SALT').hexdigest())"
```

### Sobre o projeto local

`Documents\gofile-downloader-gui` não está no git, então não dá para comparar versões dele. Se você corrigir algo lá, anote o que mudou e aplique no `GofileResolver.cs`. Vale criar um repositório para ele (`git init`) para ter esse histórico.

---

## 5. Portar uma mudança do Bunkr

O ZeroHackz/BunkrDownloader-Portable puxa as mudanças do Lysagxra/BunkrDownloader de tempos em tempos. **Verifique os dois**: o Lysagxra costuma ter as correções primeiro. Os caminhos de arquivo são diferentes entre eles.

Só a parte que **descobre os links** foi portada. Download, conexões paralelas, retomada, limite de velocidade e fila são do próprio XDM, então mudanças em `downloaders/`, `managers/`, `rate_limiter`, `dry_run` e checagem de disco podem ser ignoradas. A exceção é `_apply_item_mtime` (em `downloaders/media_downloader.py`), que põe a data de envio como data de modificação do arquivo.

A base portada hoje é o **Lysagxra**, que está à frente do ZeroHackz: Referer novo, regra de nome por prefixo, nome limpo e datas. Quando o ZeroHackz puxar essas mudanças, não há nada a portar delas.

### Onde cada coisa está

| No Python (ZeroHackz → Lysagxra) | No C# |
|---|---|
| `src/config.py` → `BUNKR_API` | `BunkrResolver.SignApi` |
| `src/config.py` → `DOWNLOAD_API` | `BunkrResolver.DownloadApi` |
| `src/config.py` → `DOWNLOAD_REFERER` | `BunkrResolver.DownloadReferer` |
| `src/config.py` → `FALLBACK_DOMAIN` | `BunkrResolver.FallbackDomain` |
| `src/config.py` → `STATUS_PAGE` | `BunkrResolver.StatusPage` |
| `src/config.py` → `HEADERS` (User-Agent) | `BunkrResolver.UserAgent` |
| `src/config.py` → `URL_TYPE_MAPPING` (`a`, `f`, `i`, `v`) | `BunkrResolver.AlbumTypes` / `FileTypes` |
| `src/config.py` → `JS_VARS_REGEX`, `MEDIA_SLUG_REGEX` | `BunkrHtml.JsVarsRegex`, `BunkrHtml.MediaSlugRegex` |
| `src/crawlers/api_utils.py` → `extract_page_vars`, `extract_file_id` | `BunkrHtml.GetPageVariables`, `BunkrHtml.GetFileId` |
| `src/crawlers/api_utils.py` → `get_download_response` | `BunkrResolver.GetUnsignedUrl` |
| `src/crawlers/api_utils.py` → `get_api_response` (assinatura do link) | `BunkrResolver.GetDownloadLink` |
| `src/crawlers/crawler_utils.py` → `extract_item_pages` (classes do `<a>`) | `BunkrHtml.ItemLinkClasses`, `GetItemPages` |
| `src/crawlers/crawler_utils.py` → `extract_next_album_pages` | `BunkrHtml.GetPageCount` |
| `src/crawlers/crawler_utils.py` → `get_item_filename` (classes do `<h1>`, e-mail da Cloudflare) | `BunkrHtml.ItemNameClasses`, `GetItemFileName`, `ReplaceCloudflareEmails` |
| `src/crawlers/crawler_utils.py` → `format_item_filename` → `_format_item_filename` | `BunkrResolver.FormatItemFileName` |
| `src/crawlers/crawler_utils.py` → `get_download_info` (`clean_name`) | `BunkrResolver.ResolveItem`, `FileHostOptions.BunkrCleanName` (caixa "nomes limpos" em Opções) |
| (só Lysagxra) `src/crawlers/date_utils.py` → `extract_item_dates` (classe `theDate`, pareada por posição com os links) | `BunkrHtml.GetItemDates` |
| (só Lysagxra) `src/crawlers/date_utils.py` → `get_item_date` | `BunkrHtml.GetItemDate` |
| (só Lysagxra) `src/crawlers/date_utils.py` → `_parse_date_string`, `_DATE_FORMATS` | `BunkrHtml.ParseDate`, `BunkrHtml.DateFormats` |
| (só Lysagxra) `src/downloaders/media_downloader.py` → `_apply_item_mtime` | `ResolvedFile.LastModified` → `FileHostFileDates` (salvo por `FileHostDownloadUIController.AddDownloads`, aplicado em `ApplicationCore.DownloadFinished`) |
| `src/url_utils.py` → `src/misc/url_utils.py` → `get_album_name` | `BunkrHtml.AlbumNameClasses`, `GetAlbumName` |
| `src/url_utils.py` → `src/misc/url_utils.py` → `check_url_type` / `resolve_url_type` | `BunkrResolver.GetUrlType`, `HostRegex` |
| `src/bunkr_utils.py` → `src/misc/bunkr_utils.py` → classes da página de status | `BunkrHtml.StatusRowClasses`, `GetServerStatus` |
| `src/general_utils.py` → `src/misc/general_utils.py` → `fetch_page` (troca de domínio no 403) | `BunkrResolver.FetchPage` |
| `src/file_utils.py` → `src/misc/file_utils.py` → `matches_ignore_list` / `matches_include_list` | `BunkrResolver.IsFilteredOut` |

Os testes em `app/XDM/XDM.FileHosts.Tests/BunkrResolverTests.cs` usam HTML de exemplo com as classes atuais. Quando mudar uma classe em `BunkrHtml.cs`, mude também no HTML de exemplo do teste.

---

## Sintoma → provável causa

| O que aparece no log da janela | Provável causa | Onde mexer |
|---|---|---|
| GoFile: "não foi possível criar uma conta de visitante" ou "recusou a listagem" | salt ou fórmula do token mudou | salt em Opções; `DefaultWebsiteTokenSalt`, `GenerateWebsiteToken` |
| GoFile: "não respondeu com JSON (HTTP 403)" | headers ou Cloudflare | `ApiHeaders` |
| GoFile: lista, mas o download falha no XDM | header ou cookie de download | `DownloadHeaders`, `AddFile` |
| Bunkr: "Nenhum arquivo encontrado no álbum" | classes do link de item mudaram | `BunkrHtml.ItemLinkClasses` |
| Bunkr: só a primeira página do álbum é listada | paginação mudou | `BunkrHtml.GetPageCount` |
| Bunkr: "Não foi possível obter o link de download" | `jsCDN`, `data-file-id` ou API de download mudou | `GetPageVariables`, `GetFileId`, `DownloadApi` |
| Bunkr: lista, mas o download falha com 403 no XDM | `Referer` ou assinatura mudou | `DownloadReferer`, `SignApi` |
| Bunkr: nomes estranhos ou só o código do CDN | classes do `<h1>` mudaram | `BunkrHtml.ItemNameClasses` |
| Bunkr: "Não foi possível carregar ... (HTTP 403)" | domínio alternativo mudou | `FallbackDomain` |
| Bunkr: a data de modificação dos arquivos é a hora do download | a classe `theDate` ou o formato da data mudou | `BunkrHtml.GetItemDates`, `DateFormats` |

---

## Pendências conhecidas

Conferido em 2026-09-16:

Nenhuma base tinha commits novos. Tudo do Lysagxra `331061a` que afeta a descoberta de links foi portado:

- **Referer do Bunkr:** `https://dl.bunkrr.cr/` (commit [`f6aad02`](https://github.com/Lysagxra/BunkrDownloader/commit/f6aad02b5397eae8197c0ecd6ef97df9fa405a63)).
- **Regra do nome:** o nome do CDN só é usado se for igual ao título ou começar com `título-` / `título_`. Antes bastava conter o título.
- **Nome limpo:** caixa em Opções, desligada por padrão. Nomes repetidos no álbum ganham `(1)`, `(2)`...
- **Data de envio:** vira a data de modificação do arquivo quando o XDM termina o download. Vale também para downloads na fila ou retomados depois de fechar o XDM.

Limites conhecidos:

1. A data da **página do item** (`get_item_date`) não foi confirmada numa página real nem pelo próprio Lysagxra. Em álbuns vale a data da listagem (`theDate`), que é confiável; links de arquivo único podem ficar sem data.
2. **GoFile:** nada pendente. O salt e a fórmula do ltsdw são os mesmos do código daqui.

---

## 6. Depois de qualquer atualização

1. Rode os testes:
   ```bash
   dotnet test app/XDM/XDM.FileHosts.Tests
   ```
2. Compile o app, com o XDM fechado (senão o `.exe` fica travado):
   ```bash
   dotnet build app/XDM/XDM.Wpf.UI/XDM.Wpf.UI.csproj -c Release
   ```
3. Se mexeu em algo compartilhado com o Linux (`IPlatformUIService`, `Config`), compile também a GTK:
   ```bash
   dotnet build app/XDM/XDM.Gtk.UI/XDM.Gtk.UI.csproj
   ```
4. Abra `app/XDM/XDM.Wpf.UI/bin/Release/net4.7.2/xdm-app.exe` e teste um link real de cada site.
5. Atualize a tabela do passo 1 com os commits novos. Este comando mostra o commit atual de cada base:
   ```bash
   for r in subhra74/xdm:master ZeroHackz/BunkrDownloader-Portable:release/latest Lysagxra/BunkrDownloader:main ltsdw/gofile-downloader:main; do
     gh api "repos/${r%%:*}/commits/${r#*:}" --jq "\"${r%%:*}  \" + .sha + \"  \" + .commit.committer.date[0:10]"
   done
   ```
   Use o commit que você **portou de fato**, não necessariamente o mais recente. Troque também os commits no comando do passo 2.
6. Faça o commit e envie para o seu fork:
   ```bash
   git add -A
   git commit -m "Sincroniza com as bases (XDM/GoFile/Bunkr)"
   git push origin master
   ```
