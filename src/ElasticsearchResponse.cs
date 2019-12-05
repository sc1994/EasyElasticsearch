using System;
using System.Linq;
using Newtonsoft.Json;

namespace EasyElasticsearch
{
    /// <summary>
    /// 实体别表响应
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ElasticsearchQueryResponse<T>
        where T : ElasticSearchQueryItem
    {
        internal ElasticsearchQueryResponse(string content)
        {
            ElasticsearchSearchResponse<T> response;
            try
            {
                response = JsonConvert.DeserializeObject<ElasticsearchSearchResponse<T>>(content);
            }
            catch (Exception ex)
            {
                throw new Exception(JsonConvert.SerializeObject(new
                {
                    Message = "反序列化失败--->" + ex.Message,
                    Source = content,
                    Target = typeof(ElasticsearchSearchResponse<T>).FullName
                }, Formatting.Indented));
            }
            Total = response.Hits?.Total;
            Took = response.Took;
            MaxScore = response.Hits?.MaxScore;
            Failures = response.Shards?.Failures;
            this.Content = response.Hits?.HitsHits?.Select(
                x =>
                {
                    var r = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(x.Source));
                    r.Id = x.Id;
                    r.Index = x.Index;
                    r.Score = x.Score;
                    r.Type = x.Type;
                    return r;
                }
            ).ToArray();
        }
        /// <summary>
        /// 总数
        /// </summary>
        public long? Total { get; }

        /// <summary>
        /// 耗时(毫秒)
        /// </summary>
        public long? Took { get; set; }

        /// <summary>
        /// 最大得分
        /// </summary>
        public decimal? MaxScore { get; set; }

        /// <summary>
        /// 以object展示搜索中出现的不致命异常
        ///     (查询结果正常呈现,但是部分索引查询失败)
        ///     如需检查错误建议将此字段序列化查询
        /// </summary>
        [JsonProperty("failures")]
        public object[] Failures { get; set; }

        /// <summary>
        /// 返回实体类
        /// </summary>
        public T[] Content { get; set; }
    }

    /// <summary>
    /// es 列表基类, 响应实体必须继承此类
    /// </summary>
    public class ElasticSearchQueryItem
    {
        /// <summary>
        /// 文档所在索引
        /// </summary>
        [JsonProperty("_index")]
        public string Index { get; set; }

        /// <summary>
        /// 文档所在类型
        /// </summary>
        [JsonProperty("_type")]
        public string Type { get; set; }

        /// <summary>
        /// 文档id
        /// </summary>
        [JsonProperty("_id")]
        public object Id { get; set; }

        /// <summary>
        /// 文档得分
        /// </summary>
        [JsonProperty("_score")]
        public decimal? Score { get; set; }
    }
}
