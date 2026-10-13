# Placar Flip 7 — app Android

App Android do **Placar Flip 7** (o placar em HTML), pronto para publicar na Google Play.

O app é uma casca nativa mínima em Kotlin com uma WebView que abre a página do placar
(`app/src/main/assets/index.html`). Tudo roda offline: as fontes Bungee e Figtree vão
dentro do app (licença SIL OFL), não há permissões, anúncios nem coleta de dados.

## O que muda em relação ao HTML original

- Fontes embutidas em `assets/fonts/` em vez do Google Fonts (funciona sem internet).
- Jogadores padrão "Jogador 1..4" (a aba mostra o número) em vez de nomes fixos.
- O botão **Voltar** do Android fecha o painel aberto (Placar, Ajustes, Corrigir rodada) e,
  sem painel aberto, sai do app.
- A tela fica acesa com o app aberto (substitui a Wake Lock API do navegador).
- A partida fica no `localStorage` de uma origem https fixa (`WebViewAssetLoader`), então
  continua salva entre aberturas e entra no backup do Android.
- Tema claro/escuro segue o sistema; barras do sistema e recorte da câmera são respeitados.

## Estrutura

```
app/src/main/assets/index.html   placar (HTML/CSS/JS)
app/src/main/assets/fonts/       fontes + licenças OFL
app/src/main/java/.../MainActivity.kt
app/src/main/res/                ícone adaptativo (com versão monocromática), tema, backup
store/graphics/                  ícone 512×512 e gráfico de destaque 1024×500 da Play Store
store/screenshots/               capturas 1920×1080 e 1080×1920 para a ficha da loja
store/listing/pt-BR/             título, descrição curta e completa
PRIVACY.md                       política de privacidade (publique numa URL)
```

## Compilar

Requisitos: Android Studio (ou JDK 17 + Android SDK 36).

```bash
./gradlew assembleDebug      # APK de teste: app/build/outputs/apk/debug/
./gradlew bundleRelease      # AAB para a Play: app/build/outputs/bundle/release/
```

Também há o workflow `.github/workflows/placar-flip7-android.yml`: a cada push nesta pasta
ele roda o lint e gera o APK de teste; se a chave de upload estiver nos secrets, gera também
o AAB assinado (baixe em *Actions → execução → Artifacts*).

## Publicar na Play Store — passo a passo

1. **Escolha o ID do app.** Hoje é `com.placarflip7.app` (em `app/build.gradle.kts`,
   `applicationId` e `namespace`). Depois do primeiro envio ele **nunca mais pode mudar**;
   troque agora se quiser algo como `com.seunome.placarflip7` (mova também a pasta do
   `MainActivity.kt` e o `package`).
2. **Crie a chave de upload** (guarde o arquivo e as senhas; sem eles não dá para atualizar o app):
   ```bash
   keytool -genkeypair -v -keystore upload-keystore.jks -alias upload \
     -keyalg RSA -keysize 2048 -validity 10000
   ```
   Para compilar localmente, copie `keystore.properties.example` para `keystore.properties`
   e preencha. Para o GitHub Actions, crie os secrets do repositório:
   `SIGNING_KEYSTORE_BASE64` (saída de `base64 -w0 upload-keystore.jks`),
   `SIGNING_STORE_PASSWORD`, `SIGNING_KEY_ALIAS` (`upload`) e `SIGNING_KEY_PASSWORD`.
3. **Gere o AAB** (`./gradlew bundleRelease` ou pelo workflow, usando *Run workflow*).
   O `versionCode` vem da variável `VERSION_CODE` (no CI, o número da execução) e precisa
   aumentar a cada envio; o `versionName` fica em `app/build.gradle.kts`.
4. **Play Console** (conta de desenvolvedor, taxa única de US$ 25): *Criar app* → nome
   "Placar Flip 7", idioma Português (Brasil), App, Gratuito.
5. **Ficha da loja:** textos de `store/listing/pt-BR/`, ícone `store/graphics/icone-512.png`,
   gráfico de destaque `store/graphics/destaque-1024x500.png` e as capturas de
   `store/screenshots/` (mínimo de 2). Categoria sugerida: *Jogos de cartas* ou *Ferramentas*.
6. **Conteúdo do app:**
   - *Política de privacidade:* publique o `PRIVACY.md` numa URL pública (ex.: GitHub Pages ou
     um Gist) e coloque seu e-mail de contato no lugar indicado.
   - *Segurança dos dados:* "O app não coleta nem compartilha dados do usuário".
   - *Anúncios:* não. *Acesso ao app:* todas as funções sem login.
   - *Classificação de conteúdo:* responda o questionário (resultado esperado: Livre).
   - *Público-alvo:* 13+ (ou 18+) evita as exigências extras de apps para crianças.
7. **Teste fechado:** contas pessoais criadas depois de nov/2023 precisam de um teste fechado
   com pelo menos 12 testadores por 14 dias antes de liberar a produção. Envie o AAB em
   *Testes → Teste fechado*, depois promova para *Produção*.
8. Ative a **Assinatura de apps do Google Play** quando solicitado (padrão para AAB).

## Atenção: marca "Flip 7"

Flip 7 é um jogo de cartas da The Op Games. A Google Play pode recusar apps que usam marca de
terceiros no nome ou no ícone sem autorização (política de *Propriedade intelectual* e de
*Falsificação de identidade*). A descrição já deixa claro que o app é não oficial, mas se a
análise reclamar, troque o nome (ex.: "Placar 7 — contador de pontos"), em
`res/values/strings.xml`, no `<title>` do `index.html` e em `store/listing/`, ou peça
autorização à editora.
