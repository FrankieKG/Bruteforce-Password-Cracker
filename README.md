# Multi-GPU Password Cracker

A high-performance, OpenCL-accelerated password cracking tool using multiple GPUs for SHA-256 brute force attacks on Windows.

## Installation

### From Releases
1. Download the latest release from the [Releases](../../releases) page.
2. Extract the archive to your desired location.
3. Ensure GPU drivers with OpenCL support are installed.
4. Run `PasswordCracking.exe` (Console) or `PasswordCrackingGUI.exe` (GUI).

### Build from Source
```bash
# Clone the repository
git clone https://github.com/your-username/bruteforcecracker-parallel.git
cd bruteforcecracker-parallel

# Build the solution
dotnet build --configuration Release

# Run console application
cd PasswordCracking/bin/Release/net7.0
./PasswordCracking.exe

# Or run GUI application
cd PasswordCrackingGUI/bin/Release/net9.0-windows
./PasswordCrackingGUI.exe
```

## Usage

### Console Application
```bash
./PasswordCracking.exe
```
- Detects GPUs, allows selection, and prompts for target hash and parameters.

### GUI Application
Run `PasswordCrackingGUI.exe` for an intuitive interface with real-time progress and GPU monitoring.

## Security & Legal Notice

**⚠️ IMPORTANT: This tool is for educational and authorized security testing purposes only.**
- Use only on systems you own or have explicit permission to test.
- Ensure compliance with local laws and regulations.
