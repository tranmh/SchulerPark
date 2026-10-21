import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Modal } from '../Modal';
import { feedbackService } from '../../services/feedbackService';
import { describeApiError } from '../../utils/apiError';
import { FeedbackSent } from './FeedbackSent';
import { MAX_COMMENT_LENGTH, primaryButtonClass, textareaClass } from './feedbackUi';

/** General feedback about the app; mailed to every admin. */
export function FeedbackModal({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const [comment, setComment] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  const submit = async () => {
    setSubmitting(true);
    setError(null);
    try {
      await feedbackService.submit({ category: 'General', comment: comment.trim() });
      setSent(true);
    } catch (err: unknown) {
      setError(describeApiError(err, 'feedback.failed'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal title={t('feedback.generalTitle')} subtitle={t('feedback.generalSubtitle')} onClose={onClose}>
      {sent ? (
        <FeedbackSent onClose={onClose} />
      ) : (
        <div className="flex flex-col gap-3">
          <div>
            <label htmlFor="general-feedback-comment" className="mb-1.5 block text-[12.5px] font-medium text-ink-500">
              {t('feedback.generalComment')}
            </label>
            <textarea
              id="general-feedback-comment"
              rows={5}
              maxLength={MAX_COMMENT_LENGTH}
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              className={textareaClass}
            />
          </div>
          {error && (
            <div className="rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">{error}</div>
          )}
          <div>
            <button
              type="button"
              onClick={submit}
              disabled={submitting || !comment.trim()}
              className={primaryButtonClass}
            >
              {submitting ? t('feedback.sending') : t('feedback.generalSubmit')}
            </button>
          </div>
        </div>
      )}
    </Modal>
  );
}
