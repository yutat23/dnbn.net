# メッセージプロトコル

TCP はバイトストリームなので、受信したデータをどこで1メッセージとして切るかを設定する必要があります。

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

`LengthFieldOffset` はヘッダ先頭からのバイト位置です。`LengthFieldLength` は 1、2、4 バイトを想定しています。

## バッファ上限

終端文字や長さフィールドが設定されていない場合、受信バッファが伸び続ける可能性があります。プロトコル仕様上どうしても区切りが曖昧な場合は `MaxReceiveBufferBytes` を設定してください。

```json
{
  "MaxReceiveBufferBytes": 65536
}
```

