namespace TinyTumble.Core.Save
{
    /// <summary>
    /// Хранилище JSON-документов по ключу. Тип данных должен сериализоваться JsonUtility
    /// ([Serializable], публичные поля или [SerializeField]).
    /// </summary>
    public interface ISaveService
    {
        bool Exists(string key);

        /// <summary>false, если документа нет или он повреждён (тогда в лог пишется предупреждение).</summary>
        bool TryLoad<T>(string key, out T data) where T : class, new();

        void Save<T>(string key, T data) where T : class;

        void Delete(string key);
    }
}
