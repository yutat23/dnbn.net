# Message protocols

English | [日本語](./ja/protocols.md)

TCP is a byte stream, so you must configure where one message ends and the next begins.

Terminator-based framing, or length-based framing (fixed-length or length-prefixed), is required. If neither is set, endpoint creation fails validation.

## Terminator framing

For text-oriented protocols.

```json
{
  "MessageTerminator": "\r\n"
}
```

To accept multiple terminator candidates on receive only, use `ReceiveMessageTerminator`.

```json
{
  "MessageTerminator": "\r",
  "ReceiveMessageTerminator": ["#", "?"]
}
```

When a shorter candidate is a prefix of a longer one, such as CR and CRLF, longest match wins.
If a TCP chunk ends immediately after the shorter candidate, the parser holds the receive notification until the next byte confirms whether the longer candidate matches.

## Fixed-length framing

For protocols with a fixed header length and body length.

```json
{
  "FixedHeaderLength": 4,
  "FixedBodyLength": 20
}
```

This example treats 24 bytes as one message.

## Length-prefixed variable-length framing

For protocols that encode the body length in a header length field.

```json
{
  "FixedHeaderLength": 6,
  "LengthFieldOffset": 2,
  "LengthFieldLength": 4
}
```

`LengthFieldOffset` is the byte offset from the start of the header. `LengthFieldLength` must be 1, 2, or 4.

You cannot combine terminator framing with length-based framing. You also cannot combine `FixedBodyLength` with a length field.

## Buffer limit

`MaxReceiveBufferBytes` is an extra memory cap against a missing terminator or an invalid declared length.

```json
{
  "MaxReceiveBufferBytes": 65536
}
```

When set, the value must be greater than zero. `null` means unlimited.
