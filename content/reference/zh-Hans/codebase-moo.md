---
kind: codebase
slug: moo
title: MOO
summary: 面向对象，完全从内部编辑，与其说是游戏引擎，不如说同样是一个研究与教学平台。
codebase: MOO
home: https://www.ipomoea.org/moo/
see-also: mush-mud-muck-moo
see-also: codebases/muck
---

MOO——*MUD, Object-Oriented*——把“世界自己编辑自己”这个想法推得比这个爱好里的任何东西都远。最初的
服务器 LambdaMOO 只带一个很小的 C 内核和一个数据库；用户体验到的几乎一切，都是**用 MOO 语言、在运
行中的数据库里、由使用它的人写出来的**。一个房间没有对应的源文件。

这个性质让 MOO 拥有了游戏之外的生命。整个九十年代里，它们被用于教学、会议和研究——Diversity
University、BioMOO、Jay's House——而关于 MOO 的技术文献，对这个领域的一个代码库来说，学术味重得不
成比例。

今天的部署量很小，但确确实实不是零，而留下来的那些服务器往往已经连续运行了几十年。

## 从外面看是什么样

没有 MSSP，在我们实测的那个游戏上也没有我们能解析的 `WHO`。它有的是连接画面里的一句话，写着*“one
of three players are active”*——这个爬虫里读拼写出来的数词的那一块，正是由此而来。一个只认数字的解
析器在那里根本看不到任何计数，并且会把那个游戏永远报成未知。
