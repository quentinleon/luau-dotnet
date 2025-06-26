# Luau for .NET
 High-level Luau bindings for .NET and Unity

![header](./docs/images/img-header.png)

[![NuGet](https://img.shields.io/nuget/v/Luau.svg)](https://www.nuget.org/packages/Luau)
[![Releases](https://img.shields.io/github/release/nuskey8/luau-dotnet.svg)](https://github.com/nuskey8/luau-dotnet/releases)
[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)

[English](./README.md) | 日本語

## 概要

Luau for .NETは.NET / Unityへの[Luau言語](https://luau.org/)の組み込みを可能にするライブラリです。async/awaitに対応した柔軟かつハイパフォーマンスな高レベルAPIと、C APIへのバインディングである低レベルAPIの両方を提供します。また、REPLや型定義ファイルの生成を行うCLIツールも併せて提供されています。

> [!CAUTION]
> このライブラリは現在プレビュー版として提供されています。多くのAPIは既に安定していますが、一部機能が未実装です。

## Why Luau?

Luaはアプリケーションへの埋め込みに特化した言語ですが、言語機能の乏しさや動的型付けであるため静的解析が行いにくいなどの問題があります。Luaから派生した言語であるLuauはTypeScriptのような型システムを利用できるほか、便利な構文やライブラリが多数追加されています。また、Luauは開発元であるRobloxでの実績がある言語であり、Luaに比べて盛んにメンテナンスが行われています。(Luaは5.4から長らく更新されていません)

また、Luauはサンドボックス環境の提供に重点を置かれています。ioライブラリなどの危険なAPIは事前に削除されており、安全性の面でもLuaより優れています。

さらにLuauはAOT環境でのパフォーマンスに最適化されており、非常に高速なインタプリタで動作させることが可能です。そのため、JITが許可されていない環境でも問題なく利用が可能です。

Luauに関する詳細は[公式ドキュメント](https://luau.org/why)を参照してください。

## プラットフォーム

Luau for .NETは以下のプラットフォームに対応しています。

| プラットフォーム | アーキテクチャ          | サポート | 備考 |
| ---------------- | ----------------------- | -------- | ---- |
| Windows          | x64                     | ✅        |      |
|                  | arm64                   | ❌        | WIP  |
| macOS            | x64                     | ✅        |      |
|                  | arm64  (Apple Silicon)  | ✅        |      |
|                  | Universal (x64 + arm64) | ✅        |      |
| Linux            | x64                     | ✅        |      |
|                  | arm64                   | ✅        |      |
| iOS              | arm64                   | ✅        |      |
|                  | x64                     | ✅        |      |
| Android          | arm64                   | ✅        |      |
|                  | x64                     | ✅        |      |
| Web              | wasm32                  | ✅        |      |

## インストール

### NuGet packages

Luau for .NETを利用するには.NET Standard2.1以上が必要です。パッケージはNuGetから入手できます。

### .NET CLI

```ps1
dotnet add package Luau
```

### Package Manager

```ps1
Install-Package Luau
```

### Unity

Unityの場合、Package Managerからのインストールが可能です。

1. Window > Package ManagerからPackage Managerを開く
2. 「+」ボタン > Add package from git URL
3. 以下のURLを入力する

```
https://github.com/nuskey8/luau-dotnet.git?path=src/Luau.Unity/Assets/Luau.Unity
```

あるいはPackages/manifest.jsonを開き、dependenciesブロックに以下を追記

```json
{
    "dependencies": {
        "com.nuskey.luau.unity": "https://github.com/nuskey8/luau-dotnet.git?path=src/Luau.Unity/Assets/Luau.Unity"
    }
}
```

また、依存関係である[System.Runtime.CompilerServices.Unsafe](https://www.nuget.org/packages/System.Runtime.Compilerservices.Unsafe/)と[System.Text.Json](https://www.nuget.org/packages/System.Text.Json)のdllをプロジェクトに追加する必要があります。これは[NugetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)を用いるか、NuGetからインストールした.nupkgを.zipにリネームし、展開したフォルダ内のdllをUnityプロジェクトに追加してください。

## クイックスタート

`LuauState`を用いてLuauスクリプトをC#から実行できます。

```cs
using Luau;

using var state = LuauState.Create();
var results = state.DoString("return 1 + 1");
Console.WriteLine(results[0]); // 2
```

> [!WARNING]
> `LuauState`はスレッドセーフではありません。同時に複数のスレッドからアクセスしないでください。

## LuauValue

Luaスクリプト上の値は`LuauValue`型で表現されます。`LuauValue`の値は`TryRead<T>(out T value)`または`Read<T>()`で読み取ることが可能です。

```cs
var results = state.DoString("return 1 + 1");

// double
var value = results[0].Read<double>();
```

また、`Type`プロパティから値の型を取得できます。

```cs
var results = state.DoString("return 'hello'");
Console.WriteLine(results[0].Type); // string
```

Luau-C#間の型の対応を以下に示します。

| Luau            | C#                        |
| --------------- | ------------------------- |
| `nil`           | `LuaValue.Nil`            |
| `boolean`       | `bool`                    |
| `lightuserdata` | `IntPtr`                  |
| `number`        | `double`, `float`         |
| `vector`        | `System.Numerics.Vector3` |
| `string`        | `string`                  |
| `table`         | `LuauTable`               |
| `function`      | `LuauFunction`            |
| `userdata`      | `T, LuauUserData`         |
| `thread`        | `LuauState`               |
| `buffer`        | `LuauBuffer`              |

C#側から`LuauValue`を作成する際には、変換可能な型の場合であれば暗黙的に`LuauValue`に変換されます。

```cs
LuauValue value;
value = 1.2;                 // double   ->  LuauValue
value = "foo";               // string   ->  LuauValue
value = state.CreateTable(); // LuaTable ->  LuauValue
```

### LuauTable

Luauの`table`型は`LuauTable`で表現されます。

```cs
var results = state.DoString("return { a = 1, b = 2, c = 3 }");
var table = results[0].Read<LuauTable>();

Console.WriteLine(table["a"]); // 1

foreach (KeyValuePair<LuauValue, LuauValue> kv in table)
{
    Console.WriteLine($"{kv.Key}:{kv.Value}");
}
```

C#側でtableを作成することも可能です。

```cs
LuauTable table = state.CreateTable();
table["a"] = "alpha";

state["t"] = table;
var results = state.DoString("return t['a']");
Console.WriteLine(results[0]); // alpha
```

### LuauUserData

C#の構造体をUserDataとしてLuauに渡すことが可能です。UserDataとして使う構造体はunmanagedである(参照を含まない)必要があります。

UserDataを作成するには`state.CreateUserData<T>()`を利用します。戻り値の`LuauUserData`はUserDataのポインタやサイズなどの情報を保持するハンドルです。

```cs
LuauUserData userdata = state.CreateUserData<Example>(new()
{
    Foo = 5,
    Bar = 1.5,
});

struct Example
{
    public int Foo;
    public double Bar;
}
```

UserDataを表す`LuauValue`は直接`Read<T>()`で読み取ることが可能です。

```cs
var value = state["example"]; // userdata
var example = value.Read<Example>();
```

### LuauBuffer

Luauの`buffer`型は`LuauBuffer`で表現されます。

```cs
var results = state.DoString("return buffer.fromstring('hello')");
var buffer = results[0].Read<LuauBuffer>();

Console.WriteLine(Encoding.UTF8.GetString(buffer.AsSpan())); // hello
```

C#側でbufferを作成することも可能です。

```cs
var buffer = state.CreateBuffer(10);

var span = buffer.AsSpan();
span[0] = (byte)'1';
span[1] = (byte)'2';
span[2] = (byte)'3';
span[3] = (byte)'4';
span[4] = (byte)'5';
"hello"u8.CopyTo(span[5..]);

state["b"] = buffer;
var results = state.DoString("return buffer.tostring(b)");
Console.WriteLine(results[0]); // 12345hello
```

## グローバル変数

Luauのグローバル変数は`LuauState`のインデクサを通じて読み書きが可能です。

```cs
state["a"] = 10;
var results = state.DoString("return a");
Console.WriteLine(results[0]);
```

## 同期/非同期API

`LuauState`によるLuauスクリプトの実行には、同期APIと非同期APIの両方が提供されています。

```cs
using var state = LuauState.Create();

// sync
state.DoString("foo()");

// async
await state.DoStringAsync("foo()");
```

同期APIの方がパフォーマンスや扱いやすさの点で優れていますが、実行するLuauスクリプトがC#側で定義した非同期関数を含む場合、同期APIでこれを実行すると例外が発生します。非同期処理を含む場合は非同期APIを利用してください。

## 関数

Luaの関数は`LuauFunction`型で表現されます。`LuauFunction`によってLuauの関数をC#側から呼び出したり、C#で定義した関数をLuau側から呼び出したりすることが可能です。

### Luauの関数をC#から呼び出す

```lua
-- sample.luau

local function add(a: number, b: number): number
    return a + b
end

return add
```

```cs
using var state = LuauState.Create();
var bytes = await File.ReadAllBytes("sample.luau");

var func = state.DoString(bytes)[0]
    .Read<LuauFunction>();

// 引数を与えて実行
var results = await func.InvokeAsync([1, 2]);
Console.WriteLine(results[0]); // 3
```

### C#の関数をLuauから呼び出す

`CreateFunction()`を用いてラムダ式からLuauFunctionを作成できます。これはSource Generatorで処理をコンパイル時に生成することで実現されています。

```cs
state["add"] = state.CreateFunction((double a, double b) =>
{
    return a + b;
});

// Luau側で実行
var results = state.DoString("return add(1, 2)");
Console.WriteLine(results[0]); // 3
```

また、`CreateFunction()`のラムダ式は非同期にすることが可能です。Luauが非同期関数の呼び出しを含む場合、実行には非同期APIを用いる必要があります。

```cs
state["wait"] = state.CreateFunction(async (double seconds, CancellationToken ct) =>
{
    await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
});

await state.DoStringAsync("wait(1)"); // 1秒待機する
```

> [!TIP]
> 複数の関数を定義するには`[LuauLibrary]`の利用を推奨します。詳細は[LuauLibrary](#luaulibrary)の項目を参照してください。

## スレッド / コルーチン

Luauのスレッドは`LuauState`で表現されます。

`state.CreateThread()`を用いてグローバル環境を共有するスレッドを作成できます。これは独立したLuauスクリプトを複数実行する際に便利です。

```cs
var thread = state.CreateThread();
thread.DoString("return 1 + 2");
```

またxLuauのコルーチンを`LuauState`として取得し、C#側で操作することも可能です。

```lua
-- coroutine.luau

local co = coroutine.create(function()
    for i = 1, 10 do
        print(i)
        coroutine.yield()
    end
end)

return co
```

```cs
var bytes = File.ReadAllBytes("coroutine.luau");
var results = state.DoString(bytes);
var co = results[0].Read<LuaState>();

for (int i = 0; i < 10; i++)
{
    var resumeResults = co.Resume(state);

    // coroutine.resume()と同様、成功時は最初の要素にtrue、それ以降に関数の戻り値を返す
    // 1, 2, 3, 4, ...
    Console.WriteLine(resumeResults[1]);
}
```

## ライブラリ

### 標準ライブラリ

`Open~`系のメソッドを用いることで、`LuauState`に追加するライブラリを指定できます。

```cs
using var state = LuauState.Create();
state.OpenBaseLibrary();
state.OpenMathLibrary();
state.OpenTableLibrary();
state.OpenStringLibrary();
state.OpenCoroutineLibrary();
state.OpenBit32Library();
state.OpenUtf8Library();
state.OpenOSLibrary();
state.OpenDebugLibrary();
state.OpenBufferLibrary();
state.OpenVectorLibrary();
```

全ての標準ライブラリをまとめて追加するには`OpenLibraries()`を利用します。

```cs
state.OpenLibraries();
```

### Requireライブラリ

Luauの`require()`はLuaのそれとは大きく異なる実装になっています。Luau for .NETはこれに対応するC# APIを提供しています。

`LuauRequirer`はLuauのモジュール解決を抽象化するクラスで、これを実装することで`require()`に対応したモジュールの読み込みをカスタマイズが可能です。標準では特定のディレクトリを起点に`*.luau`/`.luaurc`ファイルの探索を行う`FileSystemLuauRequirer`が提供されています。また、Unity向けにResource/Addressablesからモジュールをロードする実装も用意されています。

Requireライブラリを追加するには`OpenRequireLibrary()`を呼び出し、利用する`LuauRequier`のインスタンスを引数に指定します。

```cs
state.OpenRequireLibrary(new FileSystemLuauRequirer
{
    WorkingDirectory = "scripts/"       // 基準となるディレクトリ
    ConfigFilePath = "scripts/.luaurc"  // .luaurcのパス
});
```

> [!TIP]
> パスの指定には`.luaurc`に設定したエイリアスを利用することが推奨されます。
> 
> ```json
> {
>   "aliases": {
>      "Script": "."
>   }    
> }
> ```
>
> ```lua
> require "@Script/foo"
> ```

### LuauLibrary

`[LuauLibrary]`を用いることで独自のライブラリを簡単に作成できます。

```cs
// Source Generatorによって必要なコードが生成されるため、partialキーワードが必要
[LuauLibrary("foo")]
partial class FooLibrary
{
    [LuauMember]
    public double field = 10;

    [LuauMember("property")]
    public double Property { get; set; } = 20;

    [LuauMember("hello")]
    public static void Hello()
    {
        Console.WriteLine("hello!");
    }

    [LuauMember("echo")]
    public static void Echo(string value)
    {
        Console.WriteLine(value);
    }

    [LuauMember("getfield")]
    public double GetField()
    {
        return field;
    }
}
```

作成したライブラリは`OpenLibrary<T>()`で追加できます。

```cs
state.OpenLibrary<FooLibrary>();
```

これはLuauで以下のように利用できます。

```lua
print(foo.field)      -- 10
print(foo.property)   -- 20

foo.field = 50

foo.hello()           -- hello!
foo.echo("foo")       -- foo
print(foo.getfield()) -- 50
```

また、CLIツールを用いることでLuauの型定義ファイルを自動生成できます。詳細は[CLIツール](#cliツール)の項目を参照してください。

## バイトコード

`LuauCompiler.Compile()`を用いてLuauスクリプトをバイトコードに変換できます。これはLuauファイルを事前にコンパイルしておきたい時に便利です。

```cs
byte[] bytecode = LuauCompiler.Compile("return 1 + 2"u8);
```

これは`state.Load()`で`LuauFunction`として読み込むことが可能です。

```cs
var func = state.Load(bytecode);
var results = func.Invoke([]);
Console.WriteLine(results[0]); // 3
```

## スタック操作

`LuauState`は煩雑なスタック操作を必要としない高レベルAPIを提供していますが、スタックを直接操作するためのAPIも用意されています。

```cs
var bytecode = LuauCompiler.Compile(
    """
    function add(a: number, b:number): number
        return a + b
    end
    """u8);

state.Load(bytecode);

// 引数のPush
state.Push(state["add"]);
state.Push(10);
state.Push(20);

// 関数の呼び出し
state.Call(2, 1);

// 結果をスタックから取得
var result = state.ToNumber(-1);
state.Pop(1);
```

## Luau.Native

LuauのC APIバインディングは独立するLuau.NativeパッケージとしてNuGetで配布されています。高レベルAPIが必要ない場合はこれを利用できます。

### インストール

#### .NET CLI

```ps1
dotnet add package Luau.Native
```

#### Package Manager

```ps1
Install-Package Luau.Native
```

#### Unity

UnityではLuau.Nativeは通常のものと同一のパッケージで配布されています。

### 使い方

```cs 
using Luau.Native;
using static Luau.Native.NativeMethods;

unsafe
{
    lua_State* l = luaL_newstate();
    lua_pushnumber(l, 12.3);

    double v = lua_tonumber(l, -1);
    lua_pop(l, 1);

    lua_close(l);
}
```

## Unity

Luau.Unityパッケージでは通常のLuau for .NETの機能に加えて、Unity向けの拡張がいくつか用意されています。

### LuauAsset

Luau.Unityを導入することで、.luau拡張子のファイルをLuauAssetとして扱えるようになります。

![img](./docs/images/img-luau-asset-inspector.png)

`Precompile`にチェックを入れることで、Luauスクリプトを予めバイトコードにコンパイルしておくことが可能です。これにより実行時のオーバーヘッドを大幅に削減できます。

実行する際は`state.Execute()`にLuauAssetを引数として渡します。

```cs
using UnityEngine;
using Luau;
using Luau.Unity;

public class Example : MonoBehaviour
{
    [SerializeField] LuauAsset script;

    void Start()
    {
        using var state = LuauState.Create();
        state.Execute(script);
    }
}
```

### Resources / Addressables

Luau.UnityではResources / Addressablesに対応した`LuaRequirer`の実装が用意されています。

```cs
state.OpenRequireLibrary(ResourcesLuauRequirer.Default);
state.OpenRequireLibrary(AddressablesLuauRequirer.Default);
```

ただし、これらのRequirerでエイリアスを利用する場合にはこれを明示的に渡す必要があります。

```cs
state.OpenRequireLibrary(new ResourcesLuauRequirer
{
    Aliases =
    {
        ["Resources"] = "."
    }
});
```

## CLIツール

Luau for .NETはREPLや型チェックなどを実行可能なCLIツールを提供しています。

```ps1
dotnet tool install --global luau-cli
```

これを用いることで、Luauが提供しているREPLや型チェックなどのツールを`dotnet luau`コマンドから呼び出すことが可能になります。

```ps1
$ dotnet luau
> 1 + 2
3
```

```ps1
$ dotnet luau analyze test.luau
test.luau(1,1): TypeError: Type 'number' could not be converted into 'string'

$ dotnet luau ast test.luau
{"root":{"type":"AstStatBlock","location":"0,0 - 0,12","hasEnd":true,"body":[{"type":"AstStatReturn","location":"0,0 - 0,12","list":[{"type":"AstExprBinary","location":"0,7 - 0,12","op":"Add","left":{"type":"AstExprConstantNumber","location":"0,7 - 0,8","value":1},"right":{"type":"AstExprConstantNumber","location":"0,11 - 0,12","value":2}}]}]},"commentLocations":[]}%       

$ dotnet luau compile test.luau
Function 0 (??):
    1: return 1 + 2
LOADN R0 3
RETURN R0 1
```

また、Luau for .NET向けの拡張として、`dluau`コマンドが追加されています。これを用いて、プロジェクト内で定義された`[LuauLibrary]`を元に型定義ファイルを生成することができます。

```cs
[LuauLibrary("cmd")]
partial class Commands
{
    [LuauMember]
    public double foo;

    [LuauMember]
    public void Hello()
    {
        Console.WriteLine("Hello!");
    }

    [LuauMember("echo")]
    public static void Echo(string value)
    {
        Console.WriteLine(value);
    }
}
```

```ps1
$ dotnet luau dluau Program.cs -o libs.d.luau
```

```lua
-- libs.d.luau

declare cmd:
{
    foo: number,
    Hello: () -> (),
    echo: (value: string) -> (),
}
```

## ライセンス

このライブラリは[MITライセンス](LICENSE)の下で提供されています。
