import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { UiStateService } from './core/ui/ui-state.service';

export const liveOnlyGuard: CanActivateFn = () => {
  const ui = inject(UiStateService);
  const router = inject(Router);
  return ui.showLiveChrome() ? true : router.createUrlTree(['/dashboard']);
};
