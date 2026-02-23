# Installation

## Prerequisites

- **LM Studio** (or any OpenAI-compatible API server) — [Download LM Studio](https://lmstudio.ai/)
- A downloaded model (see [Model Setup](Model-Setup) for recommendations)

For building from source:
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

## Download a Release

Download the latest release for your platform from [GitHub Releases](https://github.com/Open-Orca-AI/openorca/releases):

| Platform | File |
|----------|------|
| Windows x64 | `openorca-win-x64.zip` |
| Linux x64 | `openorca-linux-x64.tar.gz` |
| macOS x64 | `openorca-osx-x64.tar.gz` |
| macOS Apple Silicon | `openorca-osx-arm64.tar.gz` |

### Windows

1. Download `openorca-win-x64.zip`
2. Extract to a directory (e.g., `C:\Tools\openorca\`)
3. Add the directory to your PATH:
   - Open **Settings > System > About > Advanced system settings > Environment Variables**
   - Edit `Path` under User variables and add the directory
4. Open a new terminal and run `openorca`

### Linux

```bash
# Download and extract
curl -LO https://github.com/Open-Orca-AI/openorca/releases/latest/download/openorca-linux-x64.tar.gz
tar xzf openorca-linux-x64.tar.gz

# Move to PATH
sudo mv openorca /usr/local/bin/
chmod +x /usr/local/bin/openorca
```

### macOS

```bash
# Intel Mac
curl -LO https://github.com/Open-Orca-AI/openorca/releases/latest/download/openorca-osx-x64.tar.gz
tar xzf openorca-osx-x64.tar.gz

# Apple Silicon
curl -LO https://github.com/Open-Orca-AI/openorca/releases/latest/download/openorca-osx-arm64.tar.gz
tar xzf openorca-osx-arm64.tar.gz

# Move to PATH
sudo mv openorca /usr/local/bin/
chmod +x /usr/local/bin/openorca
```

## Build from Source

```bash
git clone https://github.com/Open-Orca-AI/openorca.git
cd openorca
dotnet build
dotnet run --project src/OpenOrca.Cli
```

To publish a self-contained executable:

```bash
# Windows
dotnet publish src/OpenOrca.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Linux
dotnet publish src/OpenOrca.Cli -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true

# macOS Intel
dotnet publish src/OpenOrca.Cli -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true

# macOS Apple Silicon
dotnet publish src/OpenOrca.Cli -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

Output lands in `src/OpenOrca.Cli/bin/Release/net10.0/<rid>/publish/`.

## Set Up LM Studio

1. Download and install [LM Studio](https://lmstudio.ai/)
2. Download a model — see [Model Setup](Model-Setup) for recommendations
3. Go to the **Local Server** tab and click **Start Server**
   - Default URL: `http://localhost:1234/v1`
4. The server should show "Server started" in the logs

## Verify Installation

Run OpenOrca and use the built-in diagnostic command:

```
openorca
> /doctor
```

The `/doctor` command checks:
- LM Studio connection and loaded models
- Configuration file
- Log directory access
- Session storage
- Prompt templates
- Project instructions (ORCA.md)
- Native tool calling setting

If all checks pass, you're ready to go. Head to [Quick Start](Quick-Start) for your first session.
