/** Matches the server cap (FeedbackService.MaxCommentLength). */
export const MAX_COMMENT_LENGTH = 1000;

export const textareaClass =
  'w-full resize-y rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900 placeholder:text-ink-300';

export const primaryButtonClass =
  'min-h-11 rounded-full bg-brand-500 px-5 py-2 text-[13px] font-semibold text-white shadow-sm hover:bg-brand-600 disabled:opacity-50 sm:min-h-0';

export function outlineButtonClass(selected: boolean): string {
  return `w-full rounded-full border px-4 py-2.5 text-[13px] font-medium text-brand-700 transition-colors ${
    selected ? 'border-brand-500 bg-brand-50 ring-2 ring-brand-500/30' : 'border-brand-500/60 bg-white hover:bg-brand-50'
  }`;
}
