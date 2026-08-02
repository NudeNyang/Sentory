export function localizedCardTitle(item, translate) {
  switch (item?.generatedTitleKind) {
    case "ClipboardImage":
      return translate("clipboardImage");
    case "SavedLink":
      return translate("savedLink");
    case "Collection":
      return translate("collectionTitle", item.imageCount ?? 0, item.urlCount ?? 0);
    default:
      return item?.title ?? "";
  }
}

export function localizedCardSubtitle(item, translate) {
  switch (item?.generatedSubtitleKind) {
    case "ImageFormat":
      return translate("imageFormat", item.imageFormat || "PNG");
    case "CollectionCount":
      return translate("collectionItems", item.memberCount ?? 0);
    default:
      return item?.subtitle ?? "";
  }
}

export function localizedMemberTitle(member, translate) {
  return member?.generatedTitleKind === "Image"
    ? translate("image")
    : member?.title ?? "";
}
