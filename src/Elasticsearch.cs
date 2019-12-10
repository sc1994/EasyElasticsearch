using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Flurl.Http;
using Newtonsoft.Json;
using static Newtonsoft.Json.JsonConvert;

namespace EasyElasticsearch
{
    /// <summary>
    /// 初始化es节点(在程序的入口处设置)
    /// </summary>
    public static class ElasticsearchQuery
    {
        public static string ElasticSearchNode { get; set; }
    }

    public static class ElasticSearchExtendOperate
    {
        /// <summary>
        /// 匹配
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_"></param>
        /// <param name="__"></param>
        /// <returns></returns>
        public static bool Match(this string _, string __) { return false; }

        /// <summary>
        /// 匹配短语
        /// </summary>
        /// <param name="_"></param>
        /// <param name="__"></param>
        /// <returns></returns>
        public static bool MatchPhrase(this string _, string __) { return false; }

        /// <summary>
        /// 包含
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_"></param>
        /// <param name="__"></param>
        /// <returns></returns>
        public static bool Include(this string _, params string[] __) { return false; }

        /// <summary>
        /// 包含
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_"></param>
        /// <param name="__"></param>
        /// <returns></returns>
        public static bool Include(this int _, params int[] __) { return false; }

        /// <summary>
        /// 包含
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_"></param>
        /// <param name="__"></param>
        /// <returns></returns>
        public static bool Include(this long _, params long[] __) { return false; }

        /// <summary>
        /// 包含
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_"></param>
        /// <param name="__"></param>
        /// <returns></returns>
        public static bool Include(this decimal _, params decimal[] __) { return false; }

        /// <summary>
        /// 包含
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="_"></param>
        /// <param name="__"></param>
        /// <returns></returns>
        public static bool Include(this DateTime _, params DateTime[] __) { return false; }
    }

    public class DateTimeFormatAttribute : Attribute
    {
        internal string Format { get; }

        /// <summary>
        /// 标记DateTime的格式化
        /// </summary>
        /// <param name="format">格式</param>
        public DateTimeFormatAttribute(string format)
        {
            Format = format;
        }
    }

    /// <summary>
    /// 存储一些运行时变量, 减少计算量
    /// </summary>
    static class RuntimeStorage
    {
        internal static ConcurrentDictionary<string, string> DateTimeFormat { get; } = new ConcurrentDictionary<string, string>();

        internal static ConcurrentDictionary<string, string> JsonProperty { get; } = new ConcurrentDictionary<string, string>();
    }

    /// <summary>
    /// ES 查询
    /// </summary>
    public class ElasticsearchQuery<T>
        where T : ElasticsearchQueryItem
    {
        private readonly string _baseNode = $"{ElasticsearchQuery.ElasticSearchNode.Trim('/')}/{{0}}/_search";
        private readonly string _node;
        private readonly OperateStorage<T> _mustStorage;
        private readonly OperateStorage<T> _shouldStorage;
        private readonly EasticsearchSort<T> _sort;

        public ElasticsearchQuery(string index, int pageSize, int pageIndex = 1, string type = "")
        {
            _mustStorage = new OperateStorage<T>();
            _shouldStorage = new OperateStorage<T>();
            _sort = new EasticsearchSort<T>();
            PageIndex = pageIndex;
            PageSize = pageSize;
            var indexAndType = index;
            if (!string.IsNullOrWhiteSpace(type))
                indexAndType += $"/{type}";
            _node = string.Format(_baseNode, indexAndType);
        }

        public int PageIndex { get; set; }

        public int PageSize { get; set; }

        /// <summary>
        /// 且
        /// </summary>
        public ElasticsearchQuery<T> Must(params Expression<Func<T, bool>>[] compares)
        {
            _mustStorage.AddStorage(compares);
            return this;
        }

        /// <summary>
        /// 或
        /// </summary>
        public ElasticsearchQuery<T> Should(params Expression<Func<T, bool>>[] compares)
        {
            _shouldStorage.AddStorage(compares);
            return this;
        }

        /// <summary>
        /// 正序
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="field"></param>
        /// <returns></returns>
        public ElasticsearchQuery<T> Asc<TValue>(Expression<Func<T, TValue>> field)
        {
            _sort.AddDic(field, "ASC");
            return this;
        }

        /// <summary>
        /// 倒叙
        /// </summary>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="field"></param>
        /// <returns></returns>
        public ElasticsearchQuery<T> Desc<TValue>(Expression<Func<T, TValue>> field)
        {
            _sort.AddDic(field, "DESC");
            return this;
        }

        private ElasticsearchDslModel<ElasticsearchDslQueryBool> QueryDsl()
        {
            var dsl = NewDslModel<ElasticsearchDslQueryBool>();

            var (must, should, mustNot) = GetAllBool(
                _mustStorage.CompareDict.SelectMany(x => x).Where(x => x.operators != CompareOperator.NotEqual),
                _shouldStorage.CompareDict.SelectMany(x => x),
                _mustStorage.CompareDict.SelectMany(x => x).Where(x => x.operators == CompareOperator.NotEqual)
            );

            dsl.Query.Bool.Must = must;
            dsl.Query.Bool.Should = should;
            dsl.Query.Bool.MustNot = mustNot;

            dsl.Sort = ToSort();

            return dsl;
        }

        private ElasticsearchDslModel<ElasticsearchDslFilterBool> FilterDsl()
        {
            var dsl = NewDslModel<ElasticsearchDslFilterBool>();

            var must = _mustStorage.CompareDict.Select(
                x => GetAllBool(mustOperator: x.Where(w => w.operators != CompareOperator.NotEqual))
            );
            var mustNot = _mustStorage.CompareDict.Select(
                x => GetAllBool(mustNotOperator: x.Where(w => w.operators == CompareOperator.NotEqual))
            );
            var should = _shouldStorage.CompareDict.Select(
                x => GetAllBool(shouldOperator: x)
            );

            if (must?.Any(x => x.must != null) ?? false)
                dsl.Query.Bool.Must = must.Select(
                    x => new ElasticsearchDslFilterFormat(x.must)
                ).ToList();
            if (should?.Any(x => x.should != null) ?? false)
                dsl.Query.Bool.Should = should.Select(
                    x => new ElasticsearchDslFilterFormat(x.should)
                ).ToList();
            if (mustNot?.Any(x => x.mustNot != null) ?? false)
                dsl.Query.Bool.MustNot = mustNot.Select(
                    x => new ElasticsearchDslFilterFormat(x.mustNot)
                ).ToList();

            if (_sort.SortDic.Any())
                dsl.Sort = ToSort();

            return dsl;
        }

        private ElasticsearchDslModel<TBool> NewDslModel<TBool>()
            where TBool : new()
            => new ElasticsearchDslModel<TBool>
            {
                From = (PageIndex - 1) * PageSize,
                Size = PageSize,
                Query = new ElasticsearchDsl<TBool>
                {
                    Bool = new TBool()
                }
            };

        private (List<ElasticsearchDslBoolCompare> must, List<ElasticsearchDslBoolCompare> should, List<ElasticsearchDslBoolCompare> mustNot) GetAllBool(
            IEnumerable<(string field, CompareOperator operators, object value)> mustOperator = null,
            IEnumerable<(string field, CompareOperator operators, object value)> shouldOperator = null,
            IEnumerable<(string field, CompareOperator operators, object value)> mustNotOperator = null
        )
        {
            List<ElasticsearchDslBoolCompare> must = null;
            List<ElasticsearchDslBoolCompare> mustNot = null;
            List<ElasticsearchDslBoolCompare> should = null;

            if (mustOperator?.Any() ?? false)
                must = ToBool(mustOperator);

            if (shouldOperator?.Any() ?? false)
                should = ToBool(shouldOperator);

            if (mustNotOperator?.Any() ?? false)
                mustNot = ToBool(mustNotOperator);

            return (must, should, mustNot);
        }


        /// <summary>
        /// 查询
        ///     详见:https://es.xiaoleilu.com/054_Query_DSL/65_Queries_vs_filters.html
        /// </summary>
        /// <returns></returns>
        public async Task<ElasticsearchQueryResponse<T>> Query()
            => await PostDslAsync(QueryDsl());

        /// <summary>
        /// 过滤
        ///     详见:https://es.xiaoleilu.com/054_Query_DSL/65_Queries_vs_filters.html
        /// </summary>
        /// <returns></returns>
        public async Task<ElasticsearchQueryResponse<T>> Filter()
            => await PostDslAsync(FilterDsl());

        private async Task<ElasticsearchQueryResponse<T>> PostDslAsync(object dsl)
        {
            var msg = $@"
Start=======复制以下的内容可在shell直接运行{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}=======Start
curl -H ""Content-type:application/json"" -X POST -d '{SerializeObject(dsl, Formatting.Indented)}' {_node}?pretty
End=========复制以上的内容可在shell直接运行{DateTime.Now:yyyy-M-dd HH:mm:ss.fff}==========End
";
            Trace.WriteLine(msg);
            try
            {
                var res = await _node.PostJsonAsync(dsl);
                var resStr = await res.Content.ReadAsStringAsync();
                return new ElasticsearchQueryResponse<T>(resStr);
            }
            catch
            {
                throw new Exception("出现无法运行的异常:" + msg);
            }
        }

        private List<ElasticsearchDslBoolCompare> ToBool(IEnumerable<(string field, CompareOperator operators, object value)> compareDict)
        {
            var result = new List<ElasticsearchDslBoolCompare>();
            foreach (var item in compareDict)
            {
                ElasticsearchDslBoolCompare compare;
                switch (item.operators)
                {
                    case CompareOperator.Include: // in操作
                        compare = new ElasticsearchDslBoolCompare
                        {
                            QueryString = new ElasticsearchDslQueryBoolQueryString(item.field, item.value)
                        };
                        break;
                    case CompareOperator.Match: // 匹配操作
                        compare = new ElasticsearchDslBoolCompare
                        {
                            Match = new Dictionary<string, object>
                            {
                                { item.field, item.value }
                            }
                        };
                        break;
                    case CompareOperator.MatchPhrase: // 短语匹配
                        compare = new ElasticsearchDslBoolCompare
                        {
                            MatchPhrase = new Dictionary<string, object>
                            {
                                { item.field, item.value }
                            }
                        };
                        break;
                    case CompareOperator.GreaterThan:
                    case CompareOperator.GreaterEqual:
                    case CompareOperator.LessThan:
                    case CompareOperator.LessEqual:
                        compare = new ElasticsearchDslBoolCompare
                        {
                            Range = new Dictionary<string, Dictionary<string, object>>
                            {
                                {
                                    item.field, new Dictionary<string, object>
                                    {
                                        { ToCompareOperator(item.operators), item.value }
                                    }
                                }
                            }
                        };
                        break;
                    case 0: // 等于操作
                    default: // 默认等于操作
                        compare = new ElasticsearchDslBoolCompare
                        {
                            Term = new Dictionary<string, object>
                                {
                                    { item.field, item.value } // TODO: string 区分 keyword 类型
                                }
                        };
                        break;
                }
                result.Add(compare);
            }

            return result;
        }

        private Dictionary<string, ElasticsearchDslSort> ToSort()
        {
            if (!(_sort.SortDic?.Any() ?? false)) return null;

            var result = new Dictionary<string, ElasticsearchDslSort>();

            foreach (var (field, sortType) in _sort.SortDic)
            {
                result.Add(field, new ElasticsearchDslSort
                {
                    SortType = sortType
                });
            }

            return result;
        }

        private string ToCompareOperator(CompareOperator @operator)
        {
            switch (@operator)
            {
                case CompareOperator.GreaterThan: return "gt";
                case CompareOperator.GreaterEqual: return "gte";
                case CompareOperator.LessThan: return "lt";
                case CompareOperator.LessEqual: return "lte";
                default: throw new Exception("不支持的大小操作符,只能支持 大于, 小于, 大于等于, 小于等于");
            }
        }
    }

    internal class OperateStorage<T>
       where T : ElasticsearchQueryItem
    {
        private readonly ElasticsearchQuery<T> _search;
        internal List<List<(string field, CompareOperator operators, object value)>> CompareDict { get; }
            = new List<List<(string field, CompareOperator operators, object value)>>();

        /// <summary>
        /// 比较, 相当于 sql 的 where a = '1' and b < 2
        /// </summary>
        /// <param name="compare"></param>
        /// <returns></returns>
        public ElasticsearchQuery<T> AddStorage(params Expression<Func<T, bool>>[] compares)
        {
            var list = compares.Select(x => GetCompareContent(x.Body));
            AddDic(list);
            return _search;
        }

        private (string field, CompareOperator operators, object value) GetCompareContent(Expression expression)
        {
            if (expression is null)
            {
                throw new ArgumentNullException(nameof(expression));
            }

            var (left, @operator, rigth) = ExpressionHelper.SplitExpression(expression);
            var value = ExpressionHelper.GetValue(rigth);
            if (value is DateTime dateTime && ExpressionHelper.TryGetDateFormat(left, out var dateFormat))
            {
                value = dateTime.ToString(dateFormat); // 重新装箱, 使用格式化之后的值
            }
            var field = ExpressionHelper.GetField(left);

            return (field, @operator, value);
        }

        private void AddDic(IEnumerable<(string field, CompareOperator operators, object value)> list)
        {
            CompareDict.Add(list.ToList());
        }
    }

    class EasticsearchSort<T> where T : ElasticsearchQueryItem
    {
        internal List<(string field, string sortType)> SortDic { get; } = new List<(string field, string sortType)>();

        internal void AddDic<TValue>(Expression<Func<T, TValue>> field, string sortType)
          => SortDic.Add((ExpressionHelper.GetField(field.Body), sortType));
    }

    public enum CompareOperator
    {
        Equal = 0,
        GreaterThan = 1,
        GreaterEqual = 2,
        LessThan = 3,
        LessEqual = 4,
        NotEqual = 5,
        Match = 6,
        Include = 7,
        MatchPhrase = 8
    }

    class ExpressionHelper
    {
        /// <summary>
        /// 提取表达式字段
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        internal static string GetField(Expression expression)
        {
            if (expression is MemberExpression r)
            {
                // 父级属性
                var parent = string.Empty;
                if (r.Expression is MemberExpression)
                {
                    parent = GetField(r.Expression);
                }
                if (!string.IsNullOrWhiteSpace(parent))
                    parent += ".";

                var memberToken = GetMemberToken(r.Member);
                // json 别名
                if (!RuntimeStorage.JsonProperty.TryGetValue(memberToken, out var jsonProperty))
                {
                    var jsonPropertyAttribute = r.Member.CustomAttributes?.FirstOrDefault(x => x.AttributeType == typeof(JsonPropertyAttribute));
                    if (jsonPropertyAttribute != null)
                    {
                        jsonProperty = (string)jsonPropertyAttribute.ConstructorArguments[0].Value;
                        RuntimeStorage.JsonProperty.TryAdd(memberToken, jsonProperty);
                    }
                    else
                        jsonProperty = r.Member.Name;
                }

                return parent + jsonProperty;
            }

            throw ExpressionTypeError(new[] { typeof(MemberExpression) }, expression.GetType());
        }

        /// <summary>
        /// 提取表达式值
        ///     ConstantExpression
        ///     MemberExpression
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        internal static object GetValue(Expression expression)
        {
            object GetValue(Expression temp)
            {
                var objectMember = Expression.Convert(temp, typeof(object));
                var getterLambda = Expression.Lambda<Func<object>>(objectMember);
                var getter = getterLambda.Compile();
                return getter();
            }
            switch (expression)
            {
                case ConstantExpression constant:
                    return constant.Value;
                case MemberExpression _:
                case MethodCallExpression _:
                    return GetValue(expression);
                case UnaryExpression unary:
                    if (unary.Operand is MemberExpression)
                        return GetValue(unary.Operand);
                    throw ExpressionTypeError(new[] { typeof(MemberExpression), }, expression.GetType());
                case NewArrayExpression array:
                    return array.Expressions.Select(GetValue);
                default:
                    throw ExpressionTypeError(new[] { typeof(ConstantExpression), typeof(MemberExpression), typeof(MethodCallExpression), typeof(UnaryExpression) }, expression.GetType());
            }
        }

        /// <summary>
        /// 拆分表达式树
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        internal static (Expression left, CompareOperator @operator, Expression rigth) SplitExpression(Expression expression)
        {
            if (expression is null)
            {
                throw new ArgumentNullException(nameof(expression));
            }
            if (expression is BinaryExpression r)
                return (r.Left, ToCompareOperator(r.NodeType), r.Right);
            if (expression is MethodCallExpression m)
                return (m.Arguments[0], ToCompareOperator(m.Method), m.Arguments[1]);
            throw ExpressionTypeError(new[] { typeof(BinaryExpression), typeof(MethodCallExpression) }, expression.GetType());
        }

        /// <summary>
        /// 尝试获取表达式的时间格式化的特性
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="dateFormat"></param>
        /// <returns></returns>
        internal static bool TryGetDateFormat(Expression expression, out string dateFormat)
        {
            if (expression is MemberExpression r)
            {
                var memberToken = GetMemberToken(r.Member);
                // 时间格式化
                if (!RuntimeStorage.DateTimeFormat.TryGetValue(memberToken, out dateFormat))
                {
                    var dateFormatAttribute = r.Member.CustomAttributes?.FirstOrDefault(x => x.AttributeType == typeof(DateTimeFormatAttribute));
                    if (dateFormatAttribute == null) return false; // 没有获取到时间格式化

                    dateFormat = (string)dateFormatAttribute.ConstructorArguments[0].Value;
                    RuntimeStorage.DateTimeFormat.TryAdd(memberToken, dateFormat);
                }
                return true;
            }
            throw ExpressionTypeError(new[] { typeof(MemberExpression) }, expression.GetType());
        }

        /// <summary>
        /// 操作类型转换
        /// </summary>
        /// <param name="expressionType"></param>
        /// <returns></returns>
        private static CompareOperator ToCompareOperator(ExpressionType expressionType)
        {
            switch (expressionType)
            {
                case ExpressionType.Equal:
                    return CompareOperator.Equal;
                case ExpressionType.GreaterThan:
                    return CompareOperator.GreaterThan;
                case ExpressionType.GreaterThanOrEqual:
                    return CompareOperator.GreaterEqual;
                case ExpressionType.LessThan:
                    return CompareOperator.LessThan;
                case ExpressionType.LessThanOrEqual:
                    return CompareOperator.LessEqual;
                case ExpressionType.NotEqual:
                    return CompareOperator.NotEqual;
                default:
                    throw new Exception($"不能支持 {expressionType} 操作符, 目前支持的操作符请参考 {typeof(CompareOperator)} ;");
            }
        }

        /// <summary>
        /// 操作方法转换
        /// </summary>
        /// <param name="expressionType"></param>
        /// <returns></returns>
        private static CompareOperator ToCompareOperator(MethodInfo expressionType)
        {
            var methodSign = expressionType.ToString().Split('(')[0]; // 这里的逻辑缺乏推敲

            switch (methodSign)
            {
                case "Boolean Match":
                    return CompareOperator.Match;
                case "Boolean MatchPhrase":
                    return CompareOperator.MatchPhrase;
                case "Boolean Include":
                    return CompareOperator.Include;
                default:
                    throw new Exception($"不能支持 {methodSign} 方法, 目前支持的方法请参考 {typeof(ElasticSearchExtendOperate)} ;");
            }
        }

        /// <summary>
        /// 表达式类型异常统一抛出格式
        /// </summary>
        /// <param name="expect"></param>
        /// <param name="reality"></param>
        /// <returns></returns>
        private static Exception ExpressionTypeError(Type[] expect, Type reality)
        {
            return new Exception($@"期望的表达式是: {string.Join(" 或 ", expect.Select(x => x.FullName))}, 却传入:{reality.FullName}
            注意: 1.表达式不能支持多层嵌套如: ""x => x.a == 1 && x.b == 't'"". 如需此操作, 重复使用 ""And"" 属性将多层表达式拆分成多个单层表达式
                  2.表达式的左边不能为常量变量或者常量变量的属性如: ""x => 1 == x.a"". 修改表达式为 ""x => x.a == 1""
                  3.表达式的的运算符不能是函数如: ""x => x.b.StartsWith('t')"". 
                  4.因DSL语句限制 ""||"" 操作需使用 ""Or"" 属性代替");
        }

        private static string GetMemberToken(MemberInfo memberInfo)
        {
            return $"{memberInfo.DeclaringType.FullName}_{memberInfo.Name}";
        }
    }

    class ElasticsearchDslModel<T>
    {
        [JsonProperty("query")]
        public ElasticsearchDsl<T> Query { get; set; }

        [JsonProperty("from")]
        public long From { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("sort", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, ElasticsearchDslSort> Sort { get; set; }
    }

    class ElasticsearchDsl<T>
    {
        [JsonProperty("bool")]
        public T Bool { get; set; }
    }

    class ElasticsearchDslQueryBool
    {
        [JsonProperty("must", NullValueHandling = NullValueHandling.Ignore)]
        public List<ElasticsearchDslBoolCompare> Must { get; set; }

        [JsonProperty("should", NullValueHandling = NullValueHandling.Ignore)]
        public List<ElasticsearchDslBoolCompare> Should { get; set; }

        [JsonProperty("must_not", NullValueHandling = NullValueHandling.Ignore)]
        public List<ElasticsearchDslBoolCompare> MustNot { get; set; }
    }

    class ElasticsearchDslFilterBool
    {
        [JsonProperty("must", NullValueHandling = NullValueHandling.Ignore)]
        public List<ElasticsearchDslFilterFormat> Must { get; set; }

        [JsonProperty("should", NullValueHandling = NullValueHandling.Ignore)]
        public List<ElasticsearchDslFilterFormat> Should { get; set; }

        [JsonProperty("must_not", NullValueHandling = NullValueHandling.Ignore)]
        public List<ElasticsearchDslFilterFormat> MustNot { get; set; }
    }

    /// <summary>
    /// 过滤的指定格式, 没有实际意义
    /// </summary>
    class ElasticsearchDslFilterFormat
    {
        public ElasticsearchDslFilterFormat(List<ElasticsearchDslBoolCompare> compare)
        {
            Bool = new
            {
                filter = compare
            };
        }

        [JsonProperty("bool", NullValueHandling = NullValueHandling.Ignore)]
        public object Bool { get; set; }
    }

    class ElasticsearchDslBoolCompare
    {
        [JsonProperty("range", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, Dictionary<string, object>> Range { get; set; }

        [JsonProperty("term", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> Term { get; set; }

        [JsonProperty("match", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> Match { get; set; }

        [JsonProperty("match_phrase ", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> MatchPhrase { get; set; }

        [JsonProperty("query_string", NullValueHandling = NullValueHandling.Ignore)]
        public ElasticsearchDslQueryBoolQueryString QueryString { get; set; }
    }

    class ElasticsearchDslQueryBoolQueryString
    {
        public ElasticsearchDslQueryBoolQueryString(string defaultField, object query)
        {
            if (query is IEnumerable arr)
            {
                var enumerator = arr.GetEnumerator();
                var values = string.Empty;

                while (enumerator.MoveNext())
                {
                    if (enumerator.Current is string ||
                            enumerator.Current is char)
                        values += $"({enumerator.Current})  OR ";
                    else
                        values += $"{enumerator.Current}  OR ";
                }
                Query = values.TrimEnd('O', 'R', ' ');
                if (string.IsNullOrWhiteSpace(Query))
                    throw new Exception("不能传入 \"Include\" 没有任何元素的数组");
            }
            else
                throw new Exception("不能传入 \"Include\" 除集合之外的任何类型");
            DefaultField = defaultField;
        }

        [JsonProperty("default_field")]
        public string DefaultField { get; }

        [JsonProperty("query")]
        public string Query { get; }
    }

    class ElasticsearchDslSort
    {
        [JsonProperty("order")]
        public string SortType { get; set; }
    }

    class ElasticsearchSearchResponse<T>
    {
        [JsonProperty("took")]
        public long? Took { get; set; }

        [JsonProperty("timed_out")]
        public bool? TimedOut { get; set; }

        [JsonProperty("_shards")]
        public ElasticsearchResponseShards Shards { get; set; }

        [JsonProperty("hits")]
        public ElasticsearchResponseHits<T> Hits { get; set; }
    }

    class ElasticsearchResponseHits<T>
    {
        [JsonProperty("total")]
        public long? Total { get; set; }

        [JsonProperty("max_score")]
        public decimal? MaxScore { get; set; }

        [JsonProperty("hits")]
        public ElasticsearchResponseHitsHits<T>[] HitsHits { get; set; }
    }

    class ElasticsearchResponseHitsHits<T>
    {
        [JsonProperty("_index")]
        public string Index { get; set; }

        [JsonProperty("_type")]
        public string Type { get; set; }

        [JsonProperty("_id")]
        public string Id { get; set; }

        [JsonProperty("_score")]
        public decimal? Score { get; set; }

        [JsonProperty("_source")]
        public T Source { get; set; }
    }

    class ElasticsearchResponseShards
    {
        [JsonProperty("total")]
        public long? Total { get; set; }

        [JsonProperty("successful")]
        public long? Successful { get; set; }

        [JsonProperty("skipped")]
        public long? Skipped { get; set; }

        [JsonProperty("failed")]
        public long? Failed { get; set; }

        [JsonProperty("failures")]
        public object[] Failures { get; set; }
    }
}