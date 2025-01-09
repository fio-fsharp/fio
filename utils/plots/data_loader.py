import pandas as pd

data_path = 'data.csv'
base_data_path = 'data/fio/'
base_pingpong_path = base_data_path + 'pingpong-roundcount120000-30runs/'
base_threadring_path = base_data_path + 'threadring-processcount2000-roundcount1-30runs/'
base_big_path = base_data_path + 'big-processcount500-roundcount1-30runs/'
base_bang_path = base_data_path + 'bang-processcount3000-roundcount1-30runs/'
base_reversebang_path = base_data_path + 'reversebang-processcount3000-roundcount1-30runs/'

base_naive_path = 'naive/' + data_path
base_intermediate_1pc_path = 'intermediate-ewc7-bwc1-esc15/' + data_path
base_intermediate_2pc_path = 'intermediate-ewc15-bwc1-esc15/' + data_path
base_advanced_1pc_path = 'advanced-ewc7-bwc1-esc15/' + data_path
base_advanced_2pc_path = 'advanced-ewc15-bwc1-esc15/' + data_path

naive_pingpong_data_path = base_pingpong_path + base_naive_path
naive_threadring_data_path = base_threadring_path + base_naive_path
naive_big_data_path = base_big_path + base_naive_path
naive_bang_data_path = base_bang_path + base_naive_path
naive_reversebang_data_path = base_bang_path + base_naive_path

intermediate_1pc_pingpong_data_path = base_pingpong_path + base_intermediate_1pc_path
intermediate_1pc_threadring_data_path = base_threadring_path + base_intermediate_1pc_path
intermediate_1pc_big_data_path = base_big_path + base_intermediate_1pc_path
intermediate_1pc_bang_data_path = base_bang_path + base_intermediate_1pc_path
intermediate_1pc_reversebang_data_path = base_reversebang_path + base_intermediate_1pc_path

intermediate_2pc_pingpong_data_path = base_pingpong_path + base_intermediate_2pc_path
intermediate_2pc_threadring_data_path = base_threadring_path + base_intermediate_2pc_path
intermediate_2pc_big_data_path = base_big_path + base_intermediate_2pc_path
intermediate_2pc_bang_data_path = base_bang_path + base_intermediate_2pc_path
intermediate_2pc_reversebang_data_path = base_reversebang_path + base_intermediate_2pc_path

advanced_1pc_pingpong_data_path = base_pingpong_path + base_advanced_1pc_path
advanced_1pc_threadring_data_path = base_threadring_path + base_advanced_1pc_path
advanced_1pc_big_data_path = base_big_path + base_advanced_1pc_path
advanced_1pc_bang_data_path = base_bang_path + base_advanced_1pc_path
advanced_1pc_reversebang_data_path = base_reversebang_path + base_advanced_1pc_path

advanced_2pc_pingpong_data_path = base_pingpong_path + base_advanced_2pc_path
advanced_2pc_threadring_data_path = base_threadring_path + base_advanced_2pc_path
advanced_2pc_big_data_path = base_big_path + base_advanced_2pc_path
advanced_2pc_bang_data_path = base_bang_path + base_advanced_2pc_path
advanced_2pc_reversebang_data_path = base_reversebang_path + base_advanced_2pc_path

pingpongKey = 'Pingpong'
threadringKey = 'Threadring'
bigKey = 'Big'
bangKey = 'Bang'
reverseBangKey = 'ReverseBang'

naiveKey = 'Naive'
intermediateKey_1pc = 'Intermediate 1PC'
intermediateKey_2pc = 'Intermediate 2PC'
advancedKey_1pc = 'Advanced 1PC'
advancedKey_2pc = 'Advanced 2PC'

##
## Pingpong
##
pp_naive_data = pd.read_csv(naive_pingpong_data_path)['Time']
pp_intermediate_data_1pc = pd.read_csv(intermediate_1pc_pingpong_data_path)['Time']
pp_intermediate_data_2pc = pd.read_csv(intermediate_2pc_pingpong_data_path)['Time']
pp_advanced_data_1pc = pd.read_csv(advanced_1pc_pingpong_data_path)['Time']
pp_advanced_data_2pc = pd.read_csv(advanced_2pc_pingpong_data_path)['Time']

##
## ThreadRing
##
tr_naive_data = pd.read_csv(naive_threadring_data_path)['Time']
tr_intermediate_data_1pc = pd.read_csv(intermediate_1pc_threadring_data_path)['Time']
tr_intermediate_data_2pc = pd.read_csv(intermediate_2pc_threadring_data_path)['Time']
tr_advanced_data_1pc = pd.read_csv(advanced_1pc_threadring_data_path)['Time']
tr_advanced_data_2pc = pd.read_csv(advanced_2pc_threadring_data_path)['Time']

##
## Big
##
big_naive_data = pd.read_csv(naive_big_data_path)['Time']
big_intermediate_data_1pc = pd.read_csv(intermediate_1pc_big_data_path)['Time']
big_intermediate_data_2pc = pd.read_csv(intermediate_2pc_big_data_path)['Time']
big_advanced_data_1pc = pd.read_csv(advanced_1pc_big_data_path)['Time']
big_advanced_data_2pc = pd.read_csv(advanced_2pc_big_data_path)['Time']

##
## Bang
##
ba_naive_data = pd.read_csv(naive_bang_data_path)['Time']
ba_intermediate_data_1pc = pd.read_csv(intermediate_1pc_bang_data_path)['Time']
ba_intermediate_data_2pc = pd.read_csv(intermediate_2pc_bang_data_path)['Time']
ba_advanced_data_1pc = pd.read_csv(advanced_1pc_bang_data_path)['Time']
ba_advanced_data_2pc = pd.read_csv(advanced_2pc_bang_data_path)['Time']

##
## ReverseBang
##
rba_naive_data = pd.read_csv(naive_reversebang_data_path)['Time']
rba_intermediate_data_1pc = pd.read_csv(intermediate_1pc_reversebang_data_path)['Time']
rba_intermediate_data_2pc = pd.read_csv(intermediate_2pc_reversebang_data_path)['Time']
rba_advanced_data_1pc = pd.read_csv(advanced_1pc_reversebang_data_path)['Time']
rba_advanced_data_2pc = pd.read_csv(advanced_2pc_reversebang_data_path)['Time']

##
## Per Runtime
##
naive_runtime_data = { naiveKey: { pingpongKey: pp_naive_data, threadringKey: tr_naive_data, bigKey: big_naive_data, bangKey: ba_naive_data, reverseBangKey: rba_naive_data } }
intermediate_runtime_data_1pc = { intermediateKey_1pc: {pingpongKey: pp_intermediate_data_1pc, threadringKey: tr_intermediate_data_1pc, bigKey: big_intermediate_data_1pc, bangKey: ba_intermediate_data_1pc, reverseBangKey: rba_intermediate_data_1pc } }
intermediate_runtime_data_2pc = { intermediateKey_2pc: {pingpongKey: pp_intermediate_data_2pc, threadringKey: tr_intermediate_data_2pc, bigKey: big_intermediate_data_2pc, bangKey: ba_intermediate_data_2pc, reverseBangKey: rba_intermediate_data_2pc } }
advanced_runtime_data_1pc = { advancedKey_1pc: { pingpongKey: pp_advanced_data_1pc, threadringKey: tr_advanced_data_1pc, bigKey: big_intermediate_data_1pc, bangKey: ba_advanced_data_1pc, reverseBangKey: rba_advanced_data_1pc } }
advanced_runtime_data_2pc = { advancedKey_2pc: { pingpongKey: pp_advanced_data_2pc, threadringKey: tr_advanced_data_2pc, bigKey: big_intermediate_data_2pc, bangKey: ba_advanced_data_2pc, reverseBangKey: rba_advanced_data_2pc } }

##
## All Runtimes
##
all_runtime_data = [naive_runtime_data, intermediate_runtime_data_1pc, intermediate_runtime_data_2pc, advanced_runtime_data_1pc, advanced_runtime_data_2pc]
