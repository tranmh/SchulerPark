import api from './api';
import type { SubmitFeedbackRequest } from '../types/feedback';

export const feedbackService = {
  /** Mailed to every admin; nothing is stored. Resolves on 202. */
  submit: (data: SubmitFeedbackRequest) =>
    api.post('/feedback', data).then(() => undefined),
};
