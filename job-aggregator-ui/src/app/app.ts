import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { JobListComponent } from './components/job-list/job-list';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, JobListComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('job-aggregator-ui');
}
