# EasyElasticsearch

简单的生成一些 elasticsearch 语句

## 初衷

初次接触es时候, 可能只是进行一些基础查询, 比如将系统的运行日志存放在es中, 需要按照一定的维度进行筛选查询, 全文关键字查询. 无需进行复杂, 个性化的查询.  
针对这样的场景实现了这个组件, 使用一些简单的bool拼接快速开始es之旅.

## 例子

> 1.定义实体

```C#
class LogModel : ElasticsearchQueryItem
{
    [JsonProperty("@timestamp")] // 定义es数据字段名(当c#字段和es字段有差别的时候,比如命名风格的差异)
    [DateTimeFormat("yyyy:MM:dd HH:mm:ss.fff")] // 定义时间类型数据风格
    public DateTime timestamp { get; set; }
    public string level { get; set; }
    public string messageTemplate { get; set; }
    public string message { get; set; }
    public Fields fields { get; set; }
}
// 无需过多理解这个类的内容, 只是我日志格式
class Fields
{
    public string module { get; set; }
    public string category { get; set; }
    public string sub_category { get; set; }
    public string msg { get; set; }
    public string filter1 { get; set; }
    public string filter2 { get; set; }
    public string ip { get; set; }
    public string app { get; set; }
}
```

> 2.设置es链接

```C#
ElasticsearchQuery.ElasticSearchNode = "http://localhost:9222";
```

> **查询一** `level`为`error`的最近一天的日志

```C#
var indexs = "logstash*"; // 从logstash开头的Indexs中查询数据
var size = 10; // 查询10条数据
var logs = await new ElasticsearchQuery<LogModel>(indexs, size)
    .Must(x => x.level == "error")
    .Must(x => x.timestamp > DateTime.Now.AddDays(-1))
    .Desc(x => x.timestamp)
    .Query();
Console.Write(logs);
```

- 不能支持`x => x.level == "error" && x.timestamp > DateTime.Now.AddDays(-1)`这样使用`&&`表示且关系的语法, 之后的例子会解释为什么不支持

> **查询二** `level`为`information`或者`ip`为 `127.1.1.2`且`level`为`error`的日志

- 使用`Filter`和`Query`的差异[看这里](https://es.xiaoleilu.com/054_Query_DSL/65_Queries_vs_filters.html)
- 上述的查询条件可能有点乱, 大致解释一下就是 全部的`information`和指定`ip`的`error`信息

```C#
var logs = await new ElasticsearchQuery<LogModel>(indexs, size)
    .Should(x => x.level == "information")
    .Should(x => x.fields.ip == "127.1.1.2",
            x => x.level == "error")
    .Desc(x => x.timestamp)
    .Filter();
Console.Write(logs);
```

- 这个例子就能看出为什么不采用`&&`符号链接多个表达式了.

> 查询三 查询`level`为`error`和`warning`和`msg`包含`Test`关键字的日志

```C#
var logs = await new ElasticsearchQuery<LogModel>(indexs, size)
    .Must(x => x.level.Include(new [] { "error", "warning" }))
    .Must(x => x.fields.msg.Match("Test"))
    .Desc(x => x.timestamp)
    .Query();
Console.Write(logs);
```

- 目前支持的函数有
    - `Match`:匹配
    - `Include`:包含
    - `MatchPhrase`:短语匹配

## 便利性

> 引用

建议的引用方式是clone项目代码到自己的解决方案下, 通过工程文件`.csproj` 添加程序集引用, 设置相对路径.

> 如何查看生成的`dsl`

运行调试. 在调试的堆栈输出中可以看到完整`dsl`
![image](https://raw.githubusercontent.com/sc1994/EasyElasticsearch/master/static/%E6%88%AA%E5%9B%BE_1575599893743.png)
