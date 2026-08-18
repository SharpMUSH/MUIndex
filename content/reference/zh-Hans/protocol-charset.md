---
kind: protocol
slug: charset
title: CHARSET
summary: RFC 2066 里用来商定编码的 telnet 选项。一个游戏中带重音符号的名字能完好走完全程，靠的就是它；而它缺席时，一些细微的故障也由此而来。
protocol: CHARSET
home: https://www.rfc-editor.org/rfc/rfc2066
see-also: protocols/ttype
see-also: connecting
see-also: codebases/tinymux
---

CHARSET 是 telnet 选项 42，由 RFC 2066 规定。一方给出一份字符集清单，另一方从中挑一个，
双方随后就字节如何映射到字符达成一致。

实际当中，这场协商要么落在 **UTF-8** 上，要么根本就没有发生。MUSH 家族协商它的比例明显高于 MUD 家族
——TinyMUX、RhostMUSH 和 PennMUSH 都协商——这反映的是一群写散文、而且文中带名字的人。

## 没有它会怎样

客户端只能猜，而通常猜的不是 ASCII 就是 Latin-1。猜 ASCII，0x7F 以上的每个字节都会变成问号；
在 UTF-8 服务器上猜 Latin-1，每个带重音的字符都会变成两个标点。这两种故障看上去都像是游戏的错，其实都不是。

对爬虫来说，这件事会在一个很具体的地方咬人。我们自己的 telnet 库把当前编码默认设成 ASCII，
而这个默认值并不是摆设——对每一台从不协商 CHARSET 的服务器（也就是它们中的绝大多数），
每一个字节都是用它来解码的。正因如此，我们才特意给它预置了一个值。

## CHARSET 唯一够不到的地方

不管 CHARSET 最后谈成了什么，MSSP 的字段名和字段值都按 ASCII 解码，因为子协商是命令而不是文本，
而规范把 CHARSET 的适用范围限定在文本上。这大概算是合规的，同时也是有损的：一个 MSSP `NAME` 为
`Café Noir` 的游戏，报出来是 `Caf? Noir`，而原始字节在我们能控制的任何环节看到它之前就已经没了。

如果你在本站某个自述字段里看到一个乱掉的字符，而游戏自己的输出里没有，原因就在这里，
并且这在我们这一侧无法还原。
