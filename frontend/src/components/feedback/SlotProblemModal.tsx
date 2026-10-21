import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Modal } from '../Modal';
import { feedbackService } from '../../services/feedbackService';
import type { Booking } from '../../types/booking';
import type { SlotProblemCategory } from '../../types/feedback';
import { describeApiError } from '../../utils/apiError';
import { LicensePlateInput } from './LicensePlateInput';
import { EMPTY_PLATE, composePlate, type PlateValue } from './plate';
import { FeedbackSent } from './FeedbackSent';
import { MAX_COMMENT_LENGTH, outlineButtonClass, primaryButtonClass, textareaClass } from './feedbackUi';

const CATEGORIES: SlotProblemCategory[] = ['SlotOccupied', 'SlotUnusable', 'SlotNotFound', 'ChargerFault'];

interface Props {
  booking: Booking;
  onClose: () => void;
}

/** "Parkplatz Problem": pick what's wrong with today's spot, add details, mail the admins. */
export function SlotProblemModal({ booking, onClose }: Props) {
  const { t } = useTranslation();
  const [category, setCategory] = useState<SlotProblemCategory | null>(null);
  const [plate, setPlate] = useState<PlateValue>(EMPTY_PLATE);
  const [comment, setComment] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  const submit = async () => {
    if (!category) return;
    setSubmitting(true);
    setError(null);
    const plateText = category === 'SlotOccupied' ? composePlate(plate) : '';
    try {
      await feedbackService.submit({
        category,
        bookingId: booking.id,
        ...(plateText ? { plateCountry: plate.country, plateType: plate.type, plateText } : {}),
        comment: comment.trim() || undefined,
      });
      setSent(true);
    } catch (err: unknown) {
      setError(describeApiError(err, 'feedback.failed'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      title={t('feedback.title')}
      subtitle={t('feedback.subtitle', { slot: booking.parkingSlotNumber, location: booking.locationName })}
      onClose={onClose}
    >
      {sent ? (
        <FeedbackSent onClose={onClose} />
      ) : !category ? (
        <div className="flex flex-col gap-2.5">
          {CATEGORIES.map((c) => (
            <button key={c} type="button" onClick={() => setCategory(c)} className={outlineButtonClass(false)}>
              {t(`feedback.categories.${c}`)}
            </button>
          ))}
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          <button
            type="button"
            onClick={() => setCategory(null)}
            className={outlineButtonClass(true)}
            title={t('feedback.changeCategory')}
          >
            {t(`feedback.categories.${category}`)}
          </button>

          {category === 'SlotOccupied' && (
            <>
              <p className="text-[13px] text-ink-700">{t('feedback.occupiedHint')}</p>
              <LicensePlateInput value={plate} onChange={setPlate} />
            </>
          )}

          <p className="text-[13px] text-ink-700">{t('feedback.commentHint')}</p>
          <div>
            <label htmlFor="slot-problem-comment" className="mb-1.5 block text-[12.5px] font-medium text-ink-500">
              {t('feedback.comment')}
            </label>
            <textarea
              id="slot-problem-comment"
              rows={3}
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
            <button type="button" onClick={submit} disabled={submitting} className={primaryButtonClass}>
              {submitting ? t('feedback.sending') : t('feedback.submit')}
            </button>
          </div>
        </div>
      )}
    </Modal>
  );
}
