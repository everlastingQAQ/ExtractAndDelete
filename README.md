## Extract & Delete 项目总体规划

## 1. 项目要实现的功能

右键 ZIP 文件 → Extract & Delete → 选择解压位置 → 解压 → 确认解压成功 → 原 ZIP 移入回收站。

若解压失败，则保留原压缩包。

## 2. 项目的大致拆分

### 2.1 V0.5：核心功能

1. 实现 ZIP 文件解压至指定目录并判断 ZIP 文件是否成功解压。

2. 实现将原 ZIP 文件移入回收站。

3. 将上述功能组合成完整的 Extract & Delete 工作流。

### 2.2 V1.0：完整可用版本

1. 实现简单 GUI。

2. 实现 Windows Explorer 右键菜单。

3. 将 Explorer / GUI 与核心工作流连接。

4. 完成整体测试。

5. 完成软件的安装与卸载。

6. 发布第一个可使用版本。

代办优化：

1. 若文件需要更高级的权限才能回收，需要以管理员运行。

2. 若文件过大无法放入回收站，显示windows原提示，即选择是否直接删除。

### 2.3 V2.0：解压能力升级

1. 将解压引擎替换为 7-Zip。

2. 扩展支持 7z、RAR、TAR 等其他压缩格式。

## 3. 项目的具体设计

### V0.5

### 3.1 总体工作流程

```context
选择zip路径
    ↓
检查是否是zip文件 → 不是zip文件 → 取消解压
    ↓
 是zip文件
    ↓
选择解压的目标路径  
    ↓
   解压 → 解压失败 → 保留zip    
    ↓
 解压成功
    ↓
 将zip文件移入回收站 → 回收失败，返回失败信息
    ↓
 回收成功，返回成功信息
```

### 3.2 代码结构设计

```context
ExtractAndDelete/
│
├── ExtractAndDelete.slnx
│
├── src/
│   │
│   ├── ExtractAndDelete.Core/
│   │   ├── ExtractAndDelete.Core.csproj
│   │   │
│   │   ├── ArchiveExtractor.cs -- 负责解压
│   │   ├── CleanupService.cs -- 负责回收
│   │   ├── ExtractionService.cs -- 负责串联解压和回收的过程
│   │   └── Results.cs -- 定义操作结果的数据类型
│   │
│   └── ExtractAndDelete.Cli/
│       ├── ExtractAndDelete.Cli.csproj
│       └── Program.cs -- 负责cli的交互
│
└── tests/
    └── ExtractAndDelete.Tests/
        └── ExtractAndDelete.Tests.csproj
```

### 3.3 文件详细规则

#### ArchiveExtractor.cs

功能：

负责将指定的 ZIP 文件解压到指定目录。

输入：

- ZIP 文件地址。

- 解压的指定目录。

输出:

- ExtractionResult。

规则：

- 文件不存在时，解压失败。

- 文件不是合法的 ZIP 文件时，解压失败。

- 解压过程异常时，解压失败。

- 不负责删除对应的 ZIP 文件。

#### CleanupService.cs

功能：

负责将指定的文件移动到回收站中。

输入：

- 文件地址。

输出：

- CleanupResult。

规则：

- 文件不存在时，回收失败。

- 无法将文件移动到回收站时，回收失败。

- 不负责判断文件是否应该被回收。

#### ExtractionService.cs

功能：

负责串通解压文件和回收原文件这两个操作。

输入：

- ZIP 文件地址。

- 解压的指定目录。

输出：

- ExtractAndDeleteResult。

规则：

- 调用 ArchiveExtractor 进行解压。

- 若解压失败，不调用 CleanupService，并返回失败。

- 若解压成功，调用 CleanupService。

- 若回收失败，返回回收失败。

- 解压和回收都成功后，返回成功。

#### Results.cs

功能：

负责定义解压，回收，工作流的操作结果的数据类型。

##### ExtractionResult

- Success -- 判断是否成功解压。

- ErrorMessage -- 若解压错误给出的报错。

##### CleanupResult

- Success -- 判断是否成功回收。

- ErrorMessage -- 若回收错误给出的报错。

##### ExtractAndDeleteResult

- Success -- 判断是否成功解压并回收。

- ErrorStage -- 判断解压错误还是回收错误。

- ErrorMessage -- 报错。

#### Program.cs

功能 ：

负责cli的输入输出。

输入：

- ZIP 文件地址。

- 解压的指定目录。

输出：

- 解压成功或失败的信息。

规则：

- 若参数数量不足，直接结束程序并提示正确的格式。

- 通过调用 ExtractionService 来完成工作。

- 不执行任何对文件的判断等相关操作。

### 3.4 V0.5 验收标准

1. 正常 ZIP 文件能够实现解压到指定目录并回收原文件。

2. 若 ZIP 文件损坏则不执行回收操作，暂时允许只解压一部分，返回错误信息。

3. 若不存在文件，则不执行解压和回收操作，返回错误信息。

4. 若无法将文件回收，则不执行回收操作，返回错误信息。

5. 在任何解压失败或回收失败的情况下，都不得永久删除原 ZIP 文件。
