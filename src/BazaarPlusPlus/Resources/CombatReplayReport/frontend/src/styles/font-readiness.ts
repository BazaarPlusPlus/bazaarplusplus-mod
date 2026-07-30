export function onDocumentFontsReady(
  callback: () => void,
): () => void {
  let active = true;
  const ready =
    (
      window as Window & {
        __BPP_VIEWER_TEST_FONT_READY__?: Promise<unknown>;
      }
    ).__BPP_VIEWER_TEST_FONT_READY__
    ?? document.fonts.ready;
  void ready.then(() => {
    if (active) callback();
  });
  return () => {
    active = false;
  };
}
