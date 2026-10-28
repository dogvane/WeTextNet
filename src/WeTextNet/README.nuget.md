# WeTextNet

WeTextProcessing 的纯 C# 文本正则化（TN）/ 逆正则化（ITN）运行时，支持中文、英文、日文。

- 纯托管实现，无原生依赖、无 P/Invoke
- 自带预编译 FST 图（wetext 0.1.8，zh/en/ja × TN/ITN），安装即用
- 与 Python `wetext` 运行时输出逐字节一致（2086 条上游官方测试用例全部通过）

## 快速开始

```bash
dotnet add package Dogvane.WeTextNet
```

```csharp
using Dogvane.WeTextNet;

var normalizer = new Normalizer(new NormalizerOptions
{
    Lang = Language.Zh,      // Auto / Zh / En / Ja
    Operator = Operator.Tn,  // Tn / Itn
});

Console.WriteLine(normalizer.Normalize("2.5平方电线，价格是12.5元"));
// 二点五平方电线，价格是十二点五元

Console.WriteLine(normalizer.Normalize("twenty three dollars and fifty cents"));
// 换 ITN 方向时输出 $23.50
```

## 加载方式

| 构造方式 | 说明 |
|---|---|
| `new Normalizer(options)` | 默认包：NuGet contentFiles 自带的 `assets/wetext.zip`（自动复制到输出目录） |
| `new Normalizer(@"wetext.zip", options)` | 显式 zip 文件（内存加载，不落盘） |
| `new Normalizer(@"wetext_fsts", options)` | 已解压的 FST 目录 |

环境变量 `WETEXTNET_ZIP` 可覆盖默认 zip 位置。

## 文档与源码

完整文档、测试语料来源、与 C++/Python 运行时的一致性说明：

https://github.com/dogvane/WeTextNet

Apache-2.0 License。
