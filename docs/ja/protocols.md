# メッセージプロトコル

[English](../protocols.md) | 日本語

TCP はバイトストリームなので、受信したデータをどこで1メッセージとして切るかを設定する必要があります。

終端文字方式、または固定長／長さフィールド方式のいずれかの設定は必須です。どちらもない場合はendpoint生成時に設定エラーになります。

## 終端文字方式

テキスト系プロトコル向けです。

```json
{
  "MessageTerminator": "\r\n"
}
```

受信時だけ複数の終端候補を扱う場合は `ReceiveMessageTerminator` を使います。

```json
{
  "MessageTerminator": "\r",
  "ReceiveMessageTerminator": ["#", "?"]
}
```

CRとCRLFのように、短い候補が長い候補の先頭と重なる場合は最長一致を優先します。
TCPチャンクが短い候補の直後で終わった場合、次の1バイトで長い候補かどうかが確定するまで受信通知を保留します。

## 固定長方式

ヘッダ長とボディ長が固定のプロトコル向けです。

```json
{
  "FixedHeaderLength": 4,
  "FixedBodyLength": 20
}
```

この例では合計24バイトを1メッセージとして扱います。

## 長さフィールド付き可変長方式

ヘッダ内の長さフィールドでボディ長を表すプロトコル向けです。

```json
{
  "FixedHeaderLength": 6,
  "LengthFieldOffset": 2,
  "LengthFieldLength": 4
}
```

`LengthFieldOffset` はヘッダ先頭からのバイト位置です。`LengthFieldLength` は 1、2、4 のいずれかです。

終端文字方式と固定長／長さフィールド方式は併用できません。`FixedBodyLength` と長さフィールドも併用できません。

## バッファ上限

`MaxReceiveBufferBytes`は、終端未到着や不正な宣言長に対する追加のメモリ上限として設定してください。

```json
{
  "MaxReceiveBufferBytes": 65536
}
```

指定する場合は1以上である必要があります。`null`は無制限です。
