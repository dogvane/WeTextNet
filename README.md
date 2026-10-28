# WeTextNet

WeTextProcessing 的纯 C# 推理运行时（TN / ITN 文本正则化与逆正则化）。

> NuGet 包 `Dogvane.WeTextNet` 以 contentFiles 附带完整 FST 包（`assets/wetext.zip`，
> 4.4MB），安装后自动复制到输出目录，`new Normalizer()` 即可用；DLL 本体仅 31KB、
> 无嵌入资源，对 Native AOT / trimming 友好。

加载优先级：环境变量 `WETEXTNET_ZIP` → 程序集旁的 `assets/wetext.zip`（NuGet
contentFiles 的落点）。也可显式传入 zip 路径或 FST 目录构造。

加载 `pip install wetext` 自带的 OpenFst 图（`wetext/fsts/`，见下方获取方式），
复刻 Python 运行时（[pengzhendong/wetext](https://github.com/pengzhendong/wetext) 0.1.8）的完整推理管线：

```
preprocess(繁→简) → tagger FST → TokenParser 字段重排 → verbalizer FST → postprocess(全半角/去叹词/去标点/OOV)
```

**纯托管实现**：内置 OpenFst vector FST 二进制格式的解析器，以及 Compose +
ShortestPath 的完整移植（含 Tarjan SCC、AutoQueue 队列选择、堆 tie-break 语义），
不依赖任何原生库或 P/Invoke。

## 使用

SDK 自带默认 FST 包（`src/WeTextNet/assets/wetext.zip`，与 wetext 0.1.8 的
`wetext/fsts` 目录结构一致，43MB）。FST 图支持三种来源，均**内存加载**（zip 用
`System.IO.Compression.ZipArchive` 流式读取，不落盘）：

```csharp
using Dogvane.WeTextNet;

// 1) 无参构造：使用内置 assets/wetext.zip
var normalizer = new Normalizer(new NormalizerOptions
{
    Lang = Language.Zh,          // Auto / Zh / En / Ja
    Operator = Operator.Tn,      // Tn / Itn
    // 其余选项默认全 false，与 wetext 一致：
    // TraditionalToSimple, FullToHalf, RemoveInterjections,
    // RemovePuncts, TagOov, Enable0To9, RemoveErhua
});

// 2) 显式传入 zip 文件（布局同 wetext/fsts）
var n2 = new Normalizer(@"O:\path\to\wetext.zip", options);

// 3) 传入已解压的 FST 目录
var n3 = new Normalizer(@"O:\path\to\wetext_fsts", options);

string spoken = normalizer.Normalize("2.5平方电线，价格是12.5元");
// 二点五平方电线，价格是十二点五元

// 也可以只跑单步：
string tagged = normalizer.Tag("2024/01/02", Language.Zh);
// date { year: "2024" month: "01" day: "02" } ...
```

说明：图按需懒加载，同一个 Normalizer 实例内按条目缓存、线程安全；zip 每次
加载新图时重新打开、不长期持有文件句柄。

## 获取 FST 文件

从安装了 `wetext` 的 Python 环境中提取（约 44MB，36 个 `.fst`）：

```bash
python -c "import wetext, pathlib; src = pathlib.Path(wetext.__file__).parent / 'fsts'; \
    shutil.copytree(src, r'O:\path\to\wetext_fsts', dirs_exist_ok=True)"
```

或直接使用 SDK 内置的 `assets/wetext.zip`（内容同上）。

## 目录结构

```
src/WeTextNet/
  Fst.cs          OpenFst vector FST 二进制读取（pynini 写出的格式）
  Transducer.cs   线性 acceptor × 规则 FST 的组合 + SingleShortestPath 移植
  OpenFst.cs      Heap / FIFO / LIFO / TopOrder / SccQueue / AutoQueue / Tarjan SCC
  TokenParser.cs  tagged 串解析与字段重排（移植自 tn/token_parser.py）
  Normalizer.cs   Normalizer / NormalizerOptions / 语言自动检测 / 管线
src/WeTextNet.slnx          解决方案（XML 格式）
src/tests/WeTextNet.Tests/
  CorpusTests.cs      2056 条端到端语料
  PipelineTests.cs    管级行为、README 示例、边界情况
  TokenParserTests.cs 移植自官方 token_parser_test.py
```

## 测试语料来源

`CorpusTests` 的 2056 条用例全部来自两个上游项目的测试数据，并逐一用
wetext 0.1.8（与本项目加载的同一套 FST）交叉验证过期望值：

| 来源 | 说明 |
|---|---|
| `PythonWeTextProcessing/tn|itn/{chinese,english,japanese}/test/data/*.txt` | 官方全量用例，筛选出与 wetext 运行时一致的子集 |
| `PythonWeText/tests/`、wetext README 示例 | 运行时行为样例 |

未纳入的用例是官方测试矩阵依赖而 wetext 0.1.8 未暴露的选项组合
（如 ITN 的 `enable_standalone_number=False`），属于上游矩阵与运行时能力差异。

## 与 C++/Python 运行时的一致性

- **等权路径选择完全一致**：OpenFst 的 `ShortestPath` 在代价相同的路径间按
  队列纪律、状态编号和弧序选择，本项目把这套语义（BFS 组合状态编号、
  byte 匹配弧先于 ε 弧、SCC 队列类型判定、Heap tie-break、首个最优终态保留）
  逐条移植，2086 条用例与 kaldifst 输出逐字节一致。
- **verbalizer 死路返回空串**：与 kaldifst `TextNormalizer` 一致（组合无接受
  路径时输出 `""`，管线继续）。
- 上游 wetext README 中的个别示例输出已过时（如 zh TN "666" 实际输出
  “六百六十六”），测试以 0.1.8 实际行为为准。

## 未实现（相对 wetext 0.1.8）

n-best 输出、`normalize_with_mapping` 对齐映射、`StreamNormalizer` 流式推理。
这些需要额外的 prefix 图搜索逻辑，按需可在此结构上扩展。
