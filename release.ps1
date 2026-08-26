<#
.SYNOPSIS
  Build, empacota e publica um release da extensão Organizador de Capturas.

.DESCRIPTION
  Fluxo completo de release em um comando:
    1. (opcional) atualiza a versão em extension.yaml e no .csproj;
    2. compila em Release;
    3. empacota o .pext com o Toolbox do Playnite;
    4. (opcional) instala a DLL na pasta de extensões do Playnite;
    5. (opcional) commita/faz push do bump de versão;
    6. cria o release no GitHub com o .pext anexado.

.PARAMETER Version
  Nova versão (ex.: 0.0.3). Se omitida, usa a versão atual do extension.yaml.

.PARAMETER Notes
  Texto das notas do release. Alternativamente use -NotesFile.

.PARAMETER NotesFile
  Caminho de um arquivo com as notas do release.

.PARAMETER PlayniteDir
  Pasta do Playnite (padrão: D:\Playnite).

.PARAMETER Install
  Também copia a DLL + extension.yaml para a pasta de extensões do Playnite (exige Playnite fechado).

.PARAMETER Commit
  Commita o bump de versão (extension.yaml + .csproj) e faz push antes de criar o release.

.PARAMETER SkipRelease
  Só compila e empacota; não cria release no GitHub.

.PARAMETER Draft
  Cria o release como rascunho (draft).

.EXAMPLE
  .\release.ps1 -Version 0.0.3 -Commit

.EXAMPLE
  .\release.ps1 -Version 0.0.3 -NotesFile notas.md -Install -Commit
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Notes,
    [string]$NotesFile,
    [string]$PlayniteDir = "D:\Playnite",
    [switch]$Install,
    [switch]$Commit,
    [switch]$SkipRelease,
    [switch]$Draft
)

$ErrorActionPreference = "Stop"
$root     = $PSScriptRoot
$proj     = Join-Path $root "GameCaptureOrganizer"
$csproj   = Join-Path $proj "GameCaptureOrganizer.csproj"
$extYaml  = Join-Path $proj "extension.yaml"
$binDir   = Join-Path $proj "bin\Release"
$distDir  = Join-Path $root "dist"
$pkgDir   = Join-Path $distDir "pkg"
$toolbox  = Join-Path $PlayniteDir "Toolbox.exe"

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "    $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "    $msg" -ForegroundColor Yellow }

# --- Pré-requisitos ---
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet não encontrado no PATH." }
if (-not (Test-Path $toolbox)) { throw "Toolbox.exe não encontrado em $toolbox (ajuste -PlayniteDir)." }
if (-not $SkipRelease -and -not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "gh (GitHub CLI) não encontrado no PATH." }

# --- 1. Versão ---
if ($Version) {
    Step "Atualizando versão para $Version"
    (Get-Content $extYaml) -replace '^Version:.*', "Version: $Version" | Set-Content $extYaml -Encoding utf8
    (Get-Content $csproj)  -replace '<Version>.*?</Version>', "<Version>$Version</Version>" | Set-Content $csproj -Encoding utf8
    Ok "extension.yaml e .csproj atualizados."
}

$verMatch = Select-String -Path $extYaml -Pattern '^Version:\s*(.+)$'
if (-not $verMatch) { throw "Não foi possível ler a versão em $extYaml." }
$version = $verMatch.Matches[0].Groups[1].Value.Trim()
$tag = "v$version"
Ok "Versão: $version   Tag: $tag"

# --- 1b. Testes ---
# Ordem obrigatoria: testes -> build -> pacote -> release. Suite vermelha para aqui.
Step "Rodando a suíte de verificação"
Push-Location (Join-Path $root "tests\OrganizerTests")
try {
    & dotnet run -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw "A suíte de verificação falhou. Nada é publicado com teste vermelho." }
} finally { Pop-Location }
Ok "Verificações passaram."

# --- 2. Build ---
Step "Compilando (Release)"
& dotnet build $csproj -c Release -v minimal
if ($LASTEXITCODE -ne 0) { throw "Falha no build." }
Ok "Build concluído."

# --- 3. Pack (.pext) ---
Step "Empacotando .pext"
if (Test-Path $distDir) { Remove-Item $distDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pkgDir | Out-Null
Copy-Item (Join-Path $binDir "GameCaptureOrganizer.dll") $pkgDir
Copy-Item (Join-Path $binDir "extension.yaml") $pkgDir
Copy-Item (Join-Path $binDir "icon.png") $pkgDir
& $toolbox pack $pkgDir $distDir
$pext = Get-ChildItem (Join-Path $distDir "*.pext") | Select-Object -First 1
if (-not $pext) { throw "Nenhum .pext gerado." }
Ok "Pacote: $($pext.Name)"

# --- 4. Instalação opcional ---
if ($Install) {
    Step "Instalando na pasta de extensões do Playnite"
    $running = Get-Process -Name "Playnite.DesktopApp","Playnite.FullscreenApp" -ErrorAction SilentlyContinue
    if ($running) {
        Warn "Playnite está aberto — feche-o e rode de novo com -Install. Pulando a instalação."
    } else {
        $dest = Join-Path $PlayniteDir "Extensions\GameCaptureOrganizer"
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        Copy-Item (Join-Path $binDir "GameCaptureOrganizer.dll") $dest -Force
        Copy-Item (Join-Path $binDir "extension.yaml") $dest -Force
        Copy-Item (Join-Path $binDir "icon.png") $dest -Force
        Ok "Instalado em $dest"
    }
}

# --- 5. Commit/push opcional do bump ---
if ($Commit) {
    Step "Commit + push do bump de versão"
    Push-Location $root
    try {
        git add "GameCaptureOrganizer/extension.yaml" "GameCaptureOrganizer/GameCaptureOrganizer.csproj"
        # Só commita se houver algo staged.
        git diff --cached --quiet
        if ($LASTEXITCODE -ne 0) {
            git -c commit.gpgsign=false commit -m "Ajusta versão para $version"
            git push origin HEAD
            Ok "Bump de versão commitado e enviado."
        } else {
            Warn "Nada para commitar (versão já estava versionada)."
        }
    } finally { Pop-Location }
}

# --- 6. Release no GitHub ---
if ($SkipRelease) {
    Step "SkipRelease ligado — não vou criar release."
    Ok "Feito. Pacote em: $($pext.FullName)"
    return
}

Step "Criando release $tag no GitHub"

if ($NotesFile) {
    if (-not (Test-Path $NotesFile)) { throw "NotesFile não encontrado: $NotesFile" }
    $notesText = Get-Content $NotesFile -Raw
} elseif ($Notes) {
    $notesText = $Notes
} else {
    $notesText = "Release $tag da extensão Organizador de Capturas.`n`nVeja o README e o histórico de commits para detalhes."
}

$notesTmp = Join-Path $env:TEMP "gco-notes-$version.md"
Set-Content -Path $notesTmp -Value $notesText -Encoding utf8

Push-Location $root
try {
    # gh escreve no stderr quando o release não existe; isole o ErrorActionPreference
    # para o stderr de comandos nativos não virar erro fatal.
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    & gh release view $tag *> $null
    $alreadyExists = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = $eap
    if ($alreadyExists) {
        throw "O release $tag já existe. Use um -Version novo ou apague o release antes."
    }

    $ghArgs = @("release", "create", $tag, $pext.FullName, "--title", $tag, "--notes-file", $notesTmp)
    if ($Draft) { $ghArgs += "--draft" }
    $ErrorActionPreference = 'Continue'
    & gh @ghArgs
    $code = $LASTEXITCODE
    $ErrorActionPreference = $eap
    if ($code -ne 0) { throw "Falha ao criar o release (gh saiu com $code)." }
} finally {
    Pop-Location
    Remove-Item $notesTmp -ErrorAction SilentlyContinue
}

Ok "Release $tag publicado."

# --- 7. Conferência depois da CI ---
#
# A tag criada pelo gh dispara o workflow, e ele roda com generate_release_notes,
# SOBRESCREVENDO o corpo escrito aqui e trocando o asset pelo dele. Ja aconteceu de sumir
# justamente o aviso de instalacao manual. Entao: espere o asset da CI e confira o corpo.
Step "Conferindo o release depois que a CI terminar"
Warn "A CI vai reempacotar o .pext e pode reescrever as notas."
Warn "Quando ela terminar, confira e, se precisar, reescreva:"
Warn "  gh release view $tag --json body,assets"
Warn "  gh release edit $tag --notes-file <arquivo>"
Warn "E confirme a versao DENTRO do .pext publicado (e um zip): extension.yaml -> Version."
