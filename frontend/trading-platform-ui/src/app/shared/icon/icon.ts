import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-icon',
  template: `
    <svg
      xmlns="http://www.w3.org/2000/svg"
      [attr.width]="size"
      [attr.height]="size"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.75"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      @switch (name) {
        @case ('logo') {
          <path d="M4 14c4-9 7-3 8 0s4 9 8 0" />
          <path d="M4 18h16" />
        }
        @case ('dashboard') {
          <rect x="3" y="3" width="7" height="9" rx="1" />
          <rect x="14" y="3" width="7" height="5" rx="1" />
          <rect x="14" y="12" width="7" height="9" rx="1" />
          <rect x="3" y="16" width="7" height="5" rx="1" />
        }
        @case ('candles') {
          <path d="M8 4v4" /><path d="M8 16v4" /><rect x="6" y="8" width="4" height="8" rx="0.5" />
          <path d="M16 6v3" /><path d="M16 15v3" /><rect x="14" y="9" width="4" height="6" rx="0.5" />
        }
        @case ('bot') {
          <rect x="5" y="8" width="14" height="10" rx="2" />
          <path d="M12 8V5" /><circle cx="9" cy="13" r="1" /><circle cx="15" cy="13" r="1" />
        }
        @case ('scan') {
          <circle cx="11" cy="11" r="7" /><path d="m20 20-3-3" /><path d="M8 11h6" />
        }
        @case ('star') {
          <polygon points="12 3 14.5 8.8 21 9.3 16.2 13.5 17.6 20 12 16.8 6.4 20 7.8 13.5 3 9.3 9.5 8.8" />
        }
        @case ('layers') {
          <path d="m12 3 9 5-9 5-9-5Z" /><path d="m3 13 9 5 9-5" />
        }
        @case ('flask') {
          <path d="M9 3h6" /><path d="M10 3v6L5.2 18a2 2 0 0 0 1.7 3h10.2a2 2 0 0 0 1.7-3L14 9V3" />
        }
        @case ('chart') {
          <path d="M4 19V5" /><path d="M4 19h16" /><path d="m7 14 4-4 3 3 5-6" />
        }
        @case ('pie') {
          <path d="M21 12A9 9 0 1 1 12 3" /><path d="M12 3v9h9" />
        }
        @case ('briefcase') {
          <rect x="3" y="7" width="18" height="13" rx="2" /><path d="M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
        }
        @case ('positions') {
          <path d="M4 19h16" /><path d="M7 16V8" /><path d="M12 16V5" /><path d="M17 16v-6" />
        }
        @case ('orders') {
          <path d="M8 6h13" /><path d="M8 12h13" /><path d="M8 18h13" /><path d="M3 6h.01" /><path d="M3 12h.01" /><path d="M3 18h.01" />
        }
        @case ('shield') {
          <path d="M12 3 5 6v6c0 4.5 3.1 7.7 7 9 3.9-1.3 7-4.5 7-9V6Z" />
        }
        @case ('plug') {
          <path d="M7 8h10" /><path d="M9 8V4" /><path d="M15 8V4" /><path d="M8 8v5a4 4 0 0 0 8 0V8" /><path d="M12 17v3" />
        }
        @case ('bell') {
          <path d="M6 8a6 6 0 1 1 12 0c0 7 3 7 3 9H3c0-2 3-2 3-9" /><path d="M10 21a2 2 0 0 0 4 0" />
        }
        @case ('settings') {
          <circle cx="12" cy="12" r="3" />
          <path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.2a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.2a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.2a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9c.3.7.9 1.2 1.5 1.3H21a2 2 0 1 1 0 4h-.2a1.7 1.7 0 0 0-1.5 1Z" />
        }
        @case ('admin') {
          <circle cx="12" cy="8" r="4" /><path d="M4 20a8 8 0 0 1 16 0" />
        }
        @case ('search') {
          <circle cx="11" cy="11" r="7" /><path d="m20 20-3.5-3.5" />
        }
        @case ('wallet') {
          <rect x="3" y="6" width="18" height="13" rx="2" /><path d="M16 12h.01" />
        }
        @case ('trend') {
          <path d="m3 17 6-6 4 4 8-8" /><path d="M14 7h7v7" />
        }
        @case ('pause') {
          <rect x="6" y="5" width="4" height="14" rx="1" /><rect x="14" y="5" width="4" height="14" rx="1" />
        }
        @case ('stop') {
          <rect x="6" y="6" width="12" height="12" rx="2" />
        }
        @case ('play') {
          <polygon points="7 5 19 12 7 19" />
        }
        @case ('gear') {
          <circle cx="12" cy="12" r="3" /><path d="M12 2v3M12 19v3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M2 12h3M19 12h3M4.9 19.1 7 17M17 7l2.1-2.1" />
        }
        @case ('menu') {
          <path d="M4 7h16M4 12h16M4 17h16" />
        }
        @case ('alert') {
          <path d="M12 9v4" /><path d="M12 17h.01" /><path d="m10.3 4.7-7 12A2 2 0 0 0 5 20h14a2 2 0 0 0 1.7-3.3l-7-12a2 2 0 0 0-3.4 0Z" />
        }
        @case ('chevron') {
          <path d="m6 9 6 6 6-6" />
        }
        @case ('plus') {
          <path d="M12 5v14M5 12h14" />
        }
        @case ('trades') {
          <path d="M4 7h9" /><path d="M4 12h16" /><path d="M4 17h7" />
        }
        @default {
          <circle cx="12" cy="12" r="9" />
        }
      }
    </svg>
  `,
})
export class IconComponent {
  @Input({ required: true }) name = '';
  @Input() size = 16;
}
