using System.Collections.ObjectModel;

namespace STIGForge.App.Helpers;

public static class ObservableCollectionExtensions
{
  /// <summary>
  /// Clears the collection and adds all items from the source.
  /// While this still fires Clear + individual Add events,
  /// it centralizes the pattern and makes future optimization easy.
  /// For collections bound to ItemsControls with virtualization enabled,
  /// WPF batches the visual updates within a single layout pass.
  /// </summary>
  public static void ReplaceAll<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
  {
    collection.Clear();
    foreach (var item in items)
      collection.Add(item);
  }
}
