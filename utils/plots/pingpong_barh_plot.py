import numpy as np
import pandas as pd
from collections import Counter
from matplotlib import pyplot as plt

# fivethirtyeight, seaborn, seaborn-notebook, ggplot, science
#plt.style.use(['science', 'pgf'])
#print(plt.style.available)


naive_data = pd.read_csv('data/pingpong-roundcount20000-1000runs/naive/data.csv')
intermediate_data = pd.read_csv('data/pingpong-roundcount20000-1000runs/intermediate-ewc8-bwc1-esc15/data.csv')
advanced_data = pd.read_csv('data/pingpong-roundcount20000-1000runs/advanced-ewc8-bwc1-esc15/data.csv')

naive_time = naive_data['Time']
intermediate_time = intermediate_data['Time']
advanced_time = advanced_data['Time']

naive_mean_time = naive_time.mean()
intermediate_mean_time = intermediate_time.mean()
advanced_mean_time = advanced_time.mean()

runtimes = ['Naive', 'Intermediate (EWC: 8 BWC: 1 ESC: 15)', 'Advanced (EWC: 8 BWC: 1 ESC: 15)']
means = [naive_mean_time, intermediate_mean_time, advanced_mean_time]

plt.barh(runtimes, means)

plt.title('Comparison (Pingpong benchmark 20000 rounds)')

plt.xlabel('Execution Time (ms) (mean over 1000 runs)')

plt.tight_layout()

plt.savefig('plots/import_data_plot.eps')

plt.show()