# Multi-GPU Password Cracker

A high-performance, OpenCL-accelerated password cracking tool that harnesses the parallel processing power of multiple GPUs for SHA-256 brute force attacks. Built with C# and OpenCL for maximum efficiency on Windows platforms.

![Multi-GPU Performance](https://img.shields.io/badge/Multi--GPU-Optimized-green)
![OpenCL](https://img.shields.io/badge/OpenCL-Accelerated-blue)
![.NET 7+](https://img.shields.io/badge/.NET-7.0%2B-purple)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)

## 🚀 Features

### **Multi-GPU Architecture**
- **Intelligent Algorithm Selection**: Automatically chooses between Simple Distribution and Dynamic Load Balancing based on GPU configuration
- **True Parallel Processing**: Each GPU operates independently with dedicated OpenCL contexts and command queues
- **Mixed GPU Support**: Optimized for both homogeneous (identical GPUs) and heterogeneous (different GPUs) setups
- **Real-time Load Balancing**: Dynamic algorithm redistributes work based on individual GPU performance

### **Advanced Performance Optimization**
- **GPU Memory Management**: Intelligent batch sizing based on GPU memory and compute capabilities
- **Optimized GPU Operations**: Direct GPU memory operations minimize CPU-GPU transfer overhead
- **Precise Progress Tracking**: Exact combination counting with decimal precision progress updates
- **Performance Metrics**: Real-time speed monitoring, ETA calculations, and throughput analysis

### **Dual Interface Design**
- **Console Application**: Full-featured command-line interface with text-based progress bars
- **WPF GUI Application**: Modern Windows interface with real-time progress visualization
- **Unified Codebase**: Both interfaces share the same optimized GPU engine

### **Robust Error Handling**
- **Graceful Degradation**: Individual GPU failures don't crash the entire operation
- **Automatic Resource Cleanup**: RAII pattern ensures proper OpenCL resource management
- **Comprehensive Logging**: Detailed error reporting for troubleshooting

## 🏗️ Architecture

### **Core Components**

```
┌─────────────────┐    ┌─────────────────┐
│   Console App   │    │    WPF GUI      │
└─────────┬───────┘    └─────────┬───────┘
          │                      │
          └──────┬─────────────┬─┘
                 │             │
         ┌───────▼─────────────▼───────┐
         │      Kernel Engine          │
         │  ┌─────────────────────────┐ │
         │  │  Algorithm Factory      │ │
         │  │  ┌─────┐    ┌─────────┐ │ │
         │  │  │Auto │    │Dynamic  │ │ │
         │  │  │Select│   │Load Bal.│ │ │
         │  │  └─────┘    └─────────┘ │ │
         │  └─────────────────────────┘ │
         └─────────────┬─────────────────┘
                       │
         ┌─────────────▼─────────────┐
         │     OpenCL Layer          │
         │ ┌─────┐ ┌─────┐ ┌─────┐   │
         │ │GPU 1│ │GPU 2│ │GPU N│   │
         │ └─────┘ └─────┘ └─────┘   │
         └───────────────────────────┘
```

### **Algorithm Strategies**

1. **Simple Distribution**
   - Equal work division among all GPUs
   - Optimal for homogeneous GPU setups
   - Lower coordination overhead
   - Best performance when all GPUs have similar capabilities

2. **Dynamic Load Balancing**
   - Performance-based work redistribution
   - Real-time monitoring of GPU efficiency
   - Optimal for heterogeneous GPU setups
   - Automatically adapts to GPU performance differences

## 📋 Requirements

### **System Requirements**
- **Operating System**: Windows 10+ (Console app may work on Linux with .NET Core)
- **Graphics Cards**: Any OpenCL 1.2+ compatible GPU
  - NVIDIA: GTX 400 series or newer
  - AMD: HD 5000 series or newer
  - Intel: HD Graphics 4000 or newer
- **Memory**: 4GB RAM minimum, 8GB+ recommended
- **.NET Runtime**: .NET 7.0+ (Console), .NET 9.0+ (GUI)

### **Development Requirements**
- **IDE**: Visual Studio 2022 or JetBrains Rider
- **SDK**: .NET 7.0 SDK
- **OpenCL**: GPU drivers with OpenCL support

## 🛠️ Installation

### **From Releases**
1. Download the latest release from the [Releases](../../releases) page
2. Extract the archive to your desired location
3. Ensure GPU drivers with OpenCL support are installed
4. Run `PasswordCracking.exe` (Console) or `PasswordCrackingGUI.exe` (GUI)

### **Build from Source**
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

## 🎯 Usage

### **Console Application**

```bash
# Basic usage with automatic GPU detection
./PasswordCracking.exe

# The application will:
# 1. Detect available GPUs
# 2. Allow GPU selection and configuration
# 3. Prompt for target hash and parameters
# 4. Execute multi-GPU brute force attack
```

**Example Session:**
```
Detected GPUs:
1. NVIDIA GeForce RTX 3070 Laptop GPU
   Memory: 8 GB
   Compute Units: 40

2. AMD Radeon RX 6800 XT
   Memory: 16 GB
   Compute Units: 72

Enter GPU numbers to use (1,2,3) or 'all': all
Selected 2 GPU(s): NVIDIA GeForce RTX 3070 Laptop GPU, AMD Radeon RX 6800 XT

=== GPU Settings Configuration ===
Batch size for NVIDIA GeForce RTX 3070 Laptop GPU (default: 1,000,000): 2000000
Memory limit % for NVIDIA GeForce RTX 3070 Laptop GPU (default: 80%): 85

Algorithm Selection: Dynamic Distribution
Reason: GPUs have different performance levels (variance: 45.2%) - dynamic distribution will optimize efficiency

Starting multi-GPU password cracking...
Overall: [████████████████░░░░░░░░░░░░░] 67.3% | 2.1B/s | 45.2s
GPU 1: [██████████████░░░░░░░] 70.1% | 1.2B/s | 57%
GPU 2: [████████████████████░] 89.4% | 896M/s | 43%

✓ Password found at 67.3% progress!
Password: "hello123"
```

### **GUI Application**

The WPF GUI provides an intuitive interface with:

- **Real-time Progress Visualization**: Live progress bars with decimal precision
- **GPU Performance Monitoring**: Individual GPU metrics and combined throughput
- **Interactive Configuration**: Easy GPU selection and settings adjustment
- **Algorithm Selection**: Manual or automatic algorithm choice
- **Results Display**: Detailed cracking results and performance statistics

**Key GUI Features:**
- Intuitive hash input with validation
- One-click GPU configuration
- Real-time progress visualization
- Detailed performance metrics
- Multi-GPU status monitoring

### **Target Hash Generation**

Generate test hashes using the built-in utility:

```csharp
// Example: Generate SHA-256 hash for testing
string password = "test123";
string hash = PasswordHasher.HashPassword(password);
// Result: "ecd71870d1963316a97e3ac3408c9835ad8cf0f3c1bc703527c30265534f75ae"
```

## ⚡ Performance

### **Performance Optimization Tips**

1. **Batch Size Tuning**
   - High-end GPUs (8GB+): 2-5M combinations per batch
   - Mid-range GPUs (4-8GB): 1-2M combinations per batch
   - Low-end GPUs (<4GB): 500K-1M combinations per batch

2. **Memory Limits**
   - Start with 80% memory usage
   - Increase to 90-95% for dedicated mining rigs
   - Reduce to 60-70% if running other applications

3. **Algorithm Selection**
   - Use **Simple Distribution** for identical GPUs
   - Use **Dynamic Load Balancing** for mixed GPU setups
   - Let **Auto-Select** choose automatically

## 🔧 Configuration

### **Advanced Settings**

Edit configuration constants in `KernelConstants.cs`:

```csharp
// Performance tuning
public const ulong DEFAULT_BATCH_SIZE = 1_000_000;
public const int DEFAULT_MEMORY_LIMIT_PERCENT = 80;
public const double PERFORMANCE_UPDATE_INTERVAL_SECONDS = 0.5;

// Search parameters
public const int MAX_PASSWORD_LENGTH_SUPPORTED = 16;
public const string ALGORITHM_AUTO_SELECT = "Auto-Select";
```

### **Custom Character Sets**

```csharp
// Common character sets
string lowercase = "abcdefghijklmnopqrstuvwxyz";
string alphanumeric = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
string full_ascii = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()";
```

## 🔒 Security & Legal Notice

**⚠️ IMPORTANT: This tool is for educational and authorized security testing purposes only.**

- **Authorized Use Only**: Only use this tool on systems you own or have explicit permission to test
- **Educational Purpose**: Designed for learning about cryptography, GPU computing, and security
- **Responsible Disclosure**: If you discover vulnerabilities, follow responsible disclosure practices
- **Legal Compliance**: Ensure compliance with local laws and regulations

**Ethical Guidelines:**
- Never use this tool for illegal activities
- Respect others' privacy and data
- Use only for improving security posture
- Consider the computational cost and environmental impact

## 🤝 Contributing

We welcome contributions! Here's how you can help:

### **Areas for Contribution**
- **Algorithm Optimization**: Improve GPU kernel efficiency
- **Cross-platform Support**: Linux and macOS compatibility
- **Additional Hash Types**: MD5, SHA-1, bcrypt support
- **UI/UX Improvements**: Enhanced GUI features
- **Documentation**: Tutorials, examples, and guides

### **Development Setup**
1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes with appropriate tests
4. Commit your changes (`git commit -m 'Add amazing feature'`)
5. Push to the branch (`git push origin feature/amazing-feature`)
6. Open a Pull Request

### **Code Style**
- Follow C# coding conventions
- Include XML documentation for public APIs
- Add unit tests for new functionality
- Ensure OpenCL kernels are well-commented

## 📊 Technical Details

### **OpenCL Optimization Techniques**
- **Work Group Optimization**: Optimal work group sizes for different GPU architectures
- **Memory Coalescing**: Aligned memory access patterns for maximum bandwidth
- **Kernel Fusion**: Combined password generation and hashing in single kernel
- **Atomic Operations**: Thread-safe password discovery with minimal overhead

### **Architecture Patterns**
- **Strategy Pattern**: Pluggable algorithm selection
- **RAII**: Automatic resource management for OpenCL objects
- **Observer Pattern**: Progress callbacks and event handling
- **Factory Pattern**: GPU and algorithm instantiation

### **Performance Features**
- **Optimized Memory Operations**: Efficient GPU memory management and data transfer
- **Asynchronous Execution**: Non-blocking GPU operations with parallel processing
- **Dynamic Work Distribution**: Real-time load balancing across multiple GPUs
- **Precise Progress Tracking**: Exact combination counting with decimal precision


**⭐ If this project helped you, please consider giving it a star!**
